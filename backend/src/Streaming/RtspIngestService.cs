using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Rtsp;
using Rtsp.Messages;
using Rtsp.Sdp;

namespace Namorix.Scout.Streaming;

public sealed class RtspIngestService(IConfiguration configuration, ILoggerFactory loggerFactory,
    ILogger<RtspIngestService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var url = configuration["RtspSpike:Url"];
        if (string.IsNullOrWhiteSpace(url))
        {
            logger.LogInformation(
                "RtspSpike:Url is not set - RTSP ingest spike disabled. " +
                "Set RtspSpike__Url=rtsp://user:pass@host:554/... to enable it.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var session = new RtspSession(url, loggerFactory);
            try
            {
                await session.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "RTSP ingest session failed - reconnecting in 5s.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private sealed class RtspSession(string configUrl, ILoggerFactory loggerFactory)
    {
        private readonly ILogger _logger = loggerFactory.CreateLogger<RtspSession>();
        private readonly ILogger<RtspListener> _listenerLogger = loggerFactory.CreateLogger<RtspListener>();
        private readonly H264RtpStats _stats = new();

        private Uri _uri = null!;
        private string _logUri = string.Empty;
        private NetworkCredential? _credential;
        private RtspTcpTransport _transport = null!;
        private RtspListener _listener = null!;
        private Authentication? _auth;
        private uint _nonceCounter;
        private string? _sessionId;
        private TaskCompletionSource<RtspResponse>? _pending;
        private RtspRequest? _inflight;

        public async Task RunAsync(CancellationToken ct)
        {
            _uri = ParseUri(configUrl);
            ParseCredential();
            _stats.Reset();

            _logger.LogInformation("Connecting to RTSP {url}", _logUri);

            var tcp = new TcpClient();
            try
            {
                await tcp.ConnectAsync(_uri.Host, _uri.Port, ct);
            }
            catch
            {
                tcp.Dispose();
                throw;
            }

            _transport = new RtspTcpTransport(tcp);
            _listener = new RtspListener(_transport, _listenerLogger);
            _listener.MessageReceived += OnMessageReceived;
            _listener.DataReceived += OnDataReceived;
            _listener.Start();

            try
            {
                var options = new RtspRequestOptions { RtspUri = _uri };
                EnsureOk(await SendWithAuthAsync(options, ct), "OPTIONS");

                var describe = new RtspRequestDescribe
                {
                    RtspUri = _uri,
                    Headers =
                    {
                        ["Accept"] = "application/sdp"
                    }
                };
                
                var describeResponse = await SendWithAuthAsync(describe, ct);
                EnsureOk(describeResponse, "DESCRIBE");

                if (describeResponse.Data.IsEmpty)
                    throw new InvalidDataException("DESCRIBE returned an empty body.");

                var sdp = ParseSdp(Encoding.UTF8.GetString(describeResponse.Data.Span));
                var video = sdp.Medias.FirstOrDefault(m => m.MediaType == Media.MediaTypes.video)
                    ?? throw new InvalidDataException("No video media found in SDP.");

                var rtpmap = FindAttribut(video, "rtpmap");
                var fmtp = FindAttribut(video, "fmtp");
                _logger.LogInformation("Media video: payloadType={pt} {rtpmap} fmtp={fmtp}",
                    video.PayloadType, rtpmap ?? "(no rtpmap)", fmtp ?? "(no fmtp)");

                var setup = new RtspRequestSetup { RtspUri = BuildSetupUri(sdp, video) };
                setup.AddTransport(RtspTransport.Parse("RTP/AVP/TCP;unicast;interleaved=0-1"));
                var setupResponse = await SendWithAuthAsync(setup, ct);
                EnsureOk(setupResponse, "SETUP");
                _sessionId = setupResponse.Session;
                _logger.LogInformation("SETUP OK. session={session} transport={transport}",
                    _sessionId,
                    setupResponse.Headers.TryGetValue(RtspHeaderNames.Transport, out var t) ? t : "(?)");

                var play = new RtspRequestPlay { RtspUri = _uri };
                if (!string.IsNullOrEmpty(_sessionId))
                    play.Session = _sessionId;
                
                EnsureOk(await SendWithAuthAsync(play, ct), "PLAY");

                _logger.LogInformation("PLAY OK - receiving stream. Stats logged every 5s (Ctrl+C to stop).");

                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
                while (await timer.WaitForNextTickAsync(ct))
                {
                    _stats.LogAndReset(_logger);
                    if (_transport.Connected)
                        continue;
                    
                    _logger.LogWarning("RTSP connection lost - closing session (service will reconnect).");
                    return;
                }
            }
            finally
            {
                SendTeardown();
                _listener.MessageReceived -= OnMessageReceived;
                _listener.DataReceived -= OnDataReceived;
                try
                {
                    _listener.Dispose();
                }
                catch
                {
                    // ignored
                }
            }
        }

        private void SendTeardown()
        {
            if (!_transport.Connected || string.IsNullOrEmpty(_sessionId)) return;

            try
            {
                _listener.SendMessage(new RtspRequestTeardown { RtspUri = _uri, Session = _sessionId });
            }
            catch
            {
                // ignored
            }
        }

        private static Uri ParseUri(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Invalid URL (expected rtsp://...): {url}");
            }
            
            return uri.Port >= 0 ? uri : new UriBuilder(uri) { Port = 554 }.Uri;
        }

        private void ParseCredential()
        {
            var userInfo = _uri.UserInfo;
            if (string.IsNullOrEmpty(userInfo))
            {
                _credential = null;
                _logUri = _uri.AbsoluteUri;
                return;
            }

            userInfo = Uri.UnescapeDataString(userInfo);
            var separator = userInfo.IndexOf(':');
            var user = separator < 0 ? userInfo : userInfo[..separator];
            var password = separator < 0 ? string.Empty : userInfo[(separator + 1)..];
            _credential = new NetworkCredential(user, password);
            _logUri = $"{_uri.Scheme}://{_uri.Host}:{_uri.Port}{_uri.AbsolutePath}";
        }

        private SdpFile ParseSdp(string sdpText)
        {
            try
            {
                using var reader = new StringReader(sdpText);
                return SdpFile.ReadLoose(reader);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse SDP:\n{sdp}", sdpText);
                throw;
            }
        }

        private static string? FindAttribut(Media media, string key) =>
            media.Attributs.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

        private Uri BuildSetupUri(SdpFile sdp, Media video)
        {
            var control = FindAttribut(video, "control")
                ?? sdp.Attributs.FirstOrDefault(a => string.Equals(a.Key, "control", StringComparison.OrdinalIgnoreCase))?.Value;

            if (string.IsNullOrWhiteSpace(control))
                return _uri;
            
            if (Uri.TryCreate(control, UriKind.Absolute, out var absolute))
                return absolute;

            var basePath = _uri.AbsolutePath;
            var slash = basePath.LastIndexOf('/');
            basePath = slash >= 0 ? basePath[..(slash + 1)] : "/";
            var path = control.StartsWith('/') ? control : basePath + control;
            return new Uri($"{_uri.Scheme}://{_uri.Authority}{path}");
        }

        private static void EnsureOk(RtspResponse response, string step)
        {
            if (response.IsOk)
                return;
            
            throw new InvalidDataException($"{step} failed: {response.ReturnCode} {response.ReturnMessage}.");
        }

        private async Task<RtspResponse> SendRequestAsync(RtspRequest request, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<RtspResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = tcs;
            _inflight = request;
            
            try
            {
                if (!_listener.SendMessage(request))
                    throw new IOException("Failed to send request - connection closed.");
                return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            }
            finally
            {
                _pending = null;
                _inflight = null;
            }
        }

        private async Task<RtspResponse> SendWithAuthAsync(RtspRequest request, CancellationToken ct)
        {
            var current = Prepare(request);
            for (var attempt = 0; ; attempt++)
            {
                var response = await SendRequestAsync(current, ct);
                if (response.ReturnCode != 401 || attempt >= 2) return response;
                if (_credential is null)
                {
                    _logger.LogWarning("Server returned 401 for {method} but the URL has no user:pass.",
                        request.RequestTyped);
                    return response;
                }

                var challenge = response.Headers.TryGetValue(RtspHeaderNames.WWWAuthenticate, out var c) ? c : null;
                if (string.IsNullOrWhiteSpace(challenge))
                {
                    _logger.LogWarning("401 for {method} but the WWW-Authenticate header is missing.",
                        request.RequestTyped);
                    return response;
                }

                try
                {
                    _auth = Authentication.Create(_credential, challenge);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not understand the WWW-Authenticate challenge from the server.");
                    return response;
                }

                _logger.LogInformation("Retrying {method} after 401 ({auth}).", request.RequestTyped, _auth.GetType().Name);
                current = Prepare(request);
            }
        }

        private RtspRequest Prepare(RtspRequest request)
        {
            if (_auth is null)
                return request;

            var prepared = (RtspRequest)request.Clone();
            _nonceCounter++;
            var uri = prepared.RtspUri?.AbsoluteUri ?? _uri.AbsoluteUri;
            prepared.Headers[RtspHeaderNames.Authorization] =
                _auth.GetResponse(_nonceCounter, uri, prepared.RequestTyped.ToString(), []);
            return prepared;
        }

        private void OnMessageReceived(object? sender, RtspChunkEventArgs e)
        {
            switch (e.Message)
            {
                case RtspResponse response:
                    if (_inflight is not null && ReferenceEquals(response.OriginalRequest, _inflight))
                    {
                        _pending?.TrySetResult(response);
                    }
                    else
                    {
                        _logger.LogDebug("Ignoring response that does not match the pending request (cseq={CSeq}).",
                            response.CSeq);
                    }
                    break;
                case RtspRequest serverRequest:
                    _logger.LogDebug("Server sent request {method} - replying 200 OK.", serverRequest.RequestTyped);
                    try { _listener.SendMessage(serverRequest.CreateResponse()); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to reply 200 to server request."); }
                    break;
            }
        }

        private void OnDataReceived(object? sender, RtspChunkEventArgs e)
        {
            if (e.Message is not RtspData data)
                return;
            
            try
            {
                if (data.Channel == 0) _stats.CountPacket(data.Data.Span);
            }
            finally
            {
                data.Dispose();
            }
        }

        private sealed class H264RtpStats
        {
            private readonly Stopwatch _clock = Stopwatch.StartNew();
            private long _packets;
            private long _bytes;
            private long _frames;
            private long _sps;
            private long _pps;
            private long _sei;
            private long _idr;

            public void Reset()
            {
                _clock.Restart();
                _packets = _bytes = _frames = _sps = _pps = _sei = _idr = 0;
            }

            public void CountPacket(ReadOnlySpan<byte> rtp)
            {
                if (rtp.Length < 12) return;

                _packets++;
                _bytes += rtp.Length;
                if ((rtp[1] & 0x80) != 0) _frames++;

                var csrcCount = rtp[0] & 0x0F;
                var hasExtension = (rtp[0] & 0x10) != 0;
                var offset = 12 + csrcCount * 4;
                if (hasExtension)
                {
                    if (rtp.Length < offset + 4) return;
                    offset += 4 + (((rtp[offset + 2] << 8) | rtp[offset + 3]) * 4);
                }
                
                if (rtp.Length <= offset)
                    return;

                var nalType = rtp[offset] & 0x1F;
                switch (nalType)
                {
                    case 7: _sps++; break;
                    case 8: _pps++; break;
                    case 6: _sei++; break;
                    case 5: _idr++; break;
                    case 24:
                    {
                        var i = offset + 1;
                        while (i + 2 <= rtp.Length)
                        {
                            var size = (rtp[i] << 8) | rtp[i + 1];
                            i += 2;
                            if (size <= 0 || i + size > rtp.Length) break;
                            CountNalType(rtp[i] & 0x1F);
                            i += size;
                        }
                        break;
                    }
                    case 28 when rtp.Length >= offset + 2:
                        if ((rtp[offset + 1] & 0x80) != 0)
                            CountNalType(rtp[offset + 1] & 0x1F);
                        break;
                    default:
                        break;
                }
            }

            private void CountNalType(int nalType)
            {
                switch (nalType)
                {
                    case 5: _idr++; break;
                    case 7: _sps++; break;
                    case 8: _pps++; break;
                    case 6: _sei++; break;
                }
            }

            public void LogAndReset(ILogger logger)
            {
                var seconds = _clock.Elapsed.TotalSeconds;
                if (_packets == 0)
                {
                    logger.LogInformation("No RTP packets (channel 0) received in {seconds:0.0}s.", seconds);
                }
                else
                {
                    var max = Math.Max(seconds, 0.001);
                    logger.LogInformation(
                        "RTP: {packets:N0} packets · {mbytes:0.00} MB · {kbps:0.0} kbps · {fps:0.0} fps · " +
                        "SPS={sps} PPS={pps} SEI={sei} IDR={idr}",
                        _packets, _bytes / 1024.0 / 1024.0, _bytes * 8 / 1000.0 / max,
                        _frames / max, _sps, _pps, _sei, _idr);
                }
                Reset();
            }
        }
    }
}
