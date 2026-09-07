using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Rtsp;
using Rtsp.Messages;
using Rtsp.Sdp;

namespace Namorix.Scout.Streaming;

public sealed class CameraRtspClient(
    Guid cameraId,
    string cameraName,
    string configUrl,
    string? configuredCredentials,
    ILoggerFactory loggerFactory) : IAsyncDisposable
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<CameraRtspClient>();
    private readonly ILogger<RtspListener> _listenerLogger = loggerFactory.CreateLogger<RtspListener>();
    private readonly CancellationTokenSource _cts = new();
    private readonly CodecState _codec = new();
    private readonly FrameStats _stats = new();

    private Task? _runTask;
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
    private H264Depacketizer? _depacketizer;

    public Guid CameraId { get; } = cameraId;

    public event Action<VideoFrame>? FrameReceived;

    public void Start() => _runTask = Task.Run(() => RunAsync(_cts.Token));

    public H264CodecSnapshot? GetCodec() => _codec.Get();

    public async Task StopAsync()
    {
        await _cts.CancelAsync();

        if (_runTask is null)
            return;

        try
        {
            await _runTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RTSP client for {name} stopped with an error.", cameraName);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts.Dispose();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RTSP session for {name} failed - reconnecting in 5s.", cameraName);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunSessionAsync(CancellationToken ct)
    {
        _auth = null;
        _nonceCounter = 0;
        _sessionId = null;
        _stats.Reset();
        _depacketizer = new H264Depacketizer(OnFrame);

        ParseConnection();

        _logger.LogInformation("Connecting to RTSP {url}", _logUri);

        using var tcp = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(5));
        await tcp.ConnectAsync(_uri.Host, _uri.Port, connectCts.Token);

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

            _logger.LogInformation("PLAY OK - receiving stream for camera {name}.", cameraName);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(ct))
            {
                _stats.LogAndReset(_logger);
                if (_transport.Connected)
                    continue;

                _logger.LogWarning("RTSP connection lost - closing session (client will reconnect).");
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
        if (!_transport.Connected || string.IsNullOrEmpty(_sessionId))
            return;

        try
        {
            _listener.SendMessage(new RtspRequestTeardown { RtspUri = _uri, Session = _sessionId });
        }
        catch
        {
            // ignored
        }
    }

    private void ParseConnection()
    {
        _uri = ParseUri(configUrl);
        var urlUserInfo = _uri.UserInfo;

        _credential = string.IsNullOrEmpty(urlUserInfo)
            ? CredentialFromString(configuredCredentials)
            : CredentialFromUserInfo(urlUserInfo);

        _logUri = new UriBuilder(_uri) { UserName = string.Empty, Password = string.Empty }.Uri.AbsoluteUri;
        _uri = new UriBuilder(_uri) { UserName = string.Empty, Password = string.Empty }.Uri;
    }

    private static NetworkCredential CredentialFromUserInfo(string userInfo)
    {
        userInfo = Uri.UnescapeDataString(userInfo);
        var separator = userInfo.IndexOf(':');
        var user = separator < 0 ? userInfo : userInfo[..separator];
        var password = separator < 0 ? string.Empty : userInfo[(separator + 1)..];
        return new NetworkCredential(user, password);
    }

    private static NetworkCredential? CredentialFromString(string? credentials)
    {
        if (string.IsNullOrEmpty(credentials))
            return null;

        var separator = credentials.IndexOf(':');
        var user = separator < 0 ? credentials : credentials[..separator];
        var password = separator < 0 ? string.Empty : credentials[(separator + 1)..];
        return new NetworkCredential(user, password);
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
            if (response.ReturnCode != 401 || attempt >= 2)
                return response;
            if (_credential is null)
            {
                _logger.LogWarning("Server returned 401 for {method} but no credentials are configured for camera {name}.",
                    request.RequestTyped, cameraName);
                return response;
            }

            var challenge = response.Headers.TryGetValue(RtspHeaderNames.WWWAuthenticate, out var c) ? c : null;
            if (string.IsNullOrWhiteSpace(challenge))
            {
                _logger.LogWarning("401 for {method} but the WWW-Authenticate header is missing.", request.RequestTyped);
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
                try
                {
                    _listener.SendMessage(serverRequest.CreateResponse());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to reply 200 to server request.");
                }

                break;
        }
    }

    private void OnDataReceived(object? sender, RtspChunkEventArgs e)
    {
        if (e.Message is not RtspData data)
            return;

        try
        {
            if (data.Channel == 0)
            {
                _stats.CountPacket(data.Data.Length);
                _depacketizer?.ParseRtp(data.Data.Span);
            }
        }
        finally
        {
            data.Dispose();
        }
    }

    private void OnFrame(VideoFrame frame)
    {
        _stats.CountFrame(frame);

        foreach (var nal in frame.Nals)
        {
            switch (nal[0] & 0x1F)
            {
                case 7:
                    _codec.SetSps(nal);
                    break;
                case 8:
                    _codec.SetPps(nal);
                    break;
            }
        }

        FrameReceived?.Invoke(frame);
    }

    private sealed class CodecState
    {
        private readonly Lock _gate = new();
        private byte[]? _sps;
        private byte[]? _pps;

        public void SetSps(byte[] nal)
        {
            lock (_gate)
            {
                _sps = nal.ToArray();
            }
        }

        public void SetPps(byte[] nal)
        {
            lock (_gate)
            {
                _pps = nal.ToArray();
            }
        }

        public H264CodecSnapshot? Get()
        {
            lock (_gate)
            {
                if (_sps is null || _pps is null || _sps.Length < 4)
                    return null;

                var profileLevelId = Convert.ToHexString(_sps.AsSpan(1, 3)).ToLowerInvariant();
                return new H264CodecSnapshot
                {
                    Sps = _sps,
                    Pps = _pps,
                    ProfileLevelId = profileLevelId,
                    SpropParameterSets =
                        $"{Convert.ToBase64String(_sps)},{Convert.ToBase64String(_pps)}",
                };
            }
        }
    }

    private sealed class FrameStats
    {
        private readonly Lock _gate = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _packets;
        private long _rtpBytes;
        private long _frames;
        private long _idr;
        private long _sps;
        private long _pps;

        public void Reset()
        {
            lock (_gate)
            {
                _clock.Restart();
                _packets = _rtpBytes = _frames = _idr = _sps = _pps = 0;
            }
        }

        public void CountPacket(int rtpBytes)
        {
            lock (_gate)
            {
                _packets++;
                _rtpBytes += rtpBytes;
            }
        }

        public void CountFrame(VideoFrame frame)
        {
            lock (_gate)
            {
                _frames++;
                if (frame.IsKeyFrame)
                    _idr++;

                foreach (var nal in frame.Nals)
                {
                    switch (nal[0] & 0x1F)
                    {
                        case 7:
                            _sps++;
                            break;
                        case 8:
                            _pps++;
                            break;
                    }
                }
            }
        }

        public void LogAndReset(ILogger logger)
        {
            lock (_gate)
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
                        "frames={frames:N0} IDR={idr} SPS={sps} PPS={pps}",
                        _packets, _rtpBytes / 1024.0 / 1024.0, _rtpBytes * 8 / 1000.0 / max,
                        _frames / max, _frames, _idr, _sps, _pps);
                }

                _packets = _rtpBytes = _frames = _idr = _sps = _pps = 0;
                _clock.Restart();
            }
        }
    }
}
