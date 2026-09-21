using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Namorix.Scout.Models;
using Rtsp;
using Rtsp.Messages;
using Rtsp.Sdp;

namespace Namorix.Scout.Streaming;

public sealed class CameraRtspClient : IAsyncDisposable
{
    private const int StallSeconds = 15;

    // A single refused handshake is common enough that reporting it straight away would have
    // the UI flash OFFLINE between two retries that both succeed, so only a run of failures
    // is treated as the camera being down.
    private const int ErrorAfterFailures = 3;

    private static readonly TimeSpan RetryMinDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryMaxDelay = TimeSpan.FromSeconds(60);

    private readonly ILogger _logger;
    private readonly ILogger<RtspListener> _listenerLogger;
    private readonly CancellationTokenSource _cts = new();
    private readonly FrameStats _stats = new();

    private readonly Lock _stateGate = new();
    private readonly Lock _configGate = new();
    private string _cameraName;
    private string _configUrl;
    private string? _configuredCredentials;
    private long _configVersion;
    private int _retryAttempt;
    private int _consecutiveFailures;
    private string? _lastError;
    private DateTimeOffset? _lastFrameAt;
    private bool _sessionLive;

    public CameraRtspClient(
        Guid cameraId,
        string cameraName,
        string configUrl,
        string? configuredCredentials,
        ILoggerFactory loggerFactory)
    {
        CameraId = cameraId;
        _cameraName = cameraName;
        _configUrl = configUrl;
        _configuredCredentials = configuredCredentials;
        _logger = loggerFactory.CreateLogger<CameraRtspClient>();
        _listenerLogger = loggerFactory.CreateLogger<RtspListener>();
    }

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

    public Guid CameraId { get; }

    public event Action<VideoFrame>? FrameReceived;

    public void Start() => _runTask = Task.Run(() => RunAsync(_cts.Token));

    // Viewer sessions hold a reference to this object and receive frames through
    // FrameReceived, so settings have to change in place: a replacement client would strand
    // every live viewer on a stopped object until it reconnected by hand. Bumping the
    // version tells the running session to let go of the camera it was told to read.
    public void UpdateConfig(string cameraName, string configUrl, string? configuredCredentials)
    {
        lock (_configGate)
        {
            _cameraName = cameraName;
            _configUrl = configUrl;
            _configuredCredentials = configuredCredentials;
            _configVersion++;
        }

        lock (_stateGate)
        {
            _lastError = null;
            _lastFrameAt = null;
        }

        // New settings, fresh start: a camera that was backing off should be retried at the
        // short delay again rather than inherit the wait for the old address.
        Interlocked.Exchange(ref _retryAttempt, 0);
        Interlocked.Exchange(ref _consecutiveFailures, 0);
    }

    private string Name()
    {
        lock (_configGate)
        {
            return _cameraName;
        }
    }

    public CameraRuntimeStatus GetStatus()
    {
        lock (_stateGate)
        {
            if (_lastError is not null)
                return new CameraRuntimeStatus(CameraRuntimeState.Failed, _lastError, _lastFrameAt);

            // Between sessions there is no stream that could be silent, and the last frame of
            // the session that ended says nothing about the one being opened - reading it as a
            // stall here would outrun the failure count and put OFFLINE back on screen.
            if (!_sessionLive)
                return new CameraRuntimeStatus(CameraRuntimeState.Connecting, null, _lastFrameAt);

            var lastFrame = _lastFrameAt;
            if (lastFrame is null)
                return new CameraRuntimeStatus(CameraRuntimeState.Connecting, null, null);

            var silence = DateTimeOffset.UtcNow - lastFrame.Value;
            if (silence <= TimeSpan.FromSeconds(StallSeconds))
                return new CameraRuntimeStatus(CameraRuntimeState.Streaming, null, lastFrame);

            // PLAY succeeded, so the camera going quiet is not an exception anyone can
            // catch - the socket stays open and the loop sees nothing wrong. Age out the
            // "streaming" claim instead of trusting the last successful handshake.
            return new CameraRuntimeStatus(CameraRuntimeState.Failed,
                $"No video received for {silence.TotalSeconds:0}s.", lastFrame);
        }
    }

    private void SetError(string message)
    {
        lock (_stateGate)
        {
            _lastError = message;
        }
    }

    // The exception text is surfaced to the UI, so the cases users actually hit get a
    // sentence instead of a framework string.
    private static string DescribeFailure(Exception ex) => ex switch
    {
        TimeoutException => "The camera did not answer in time.",
        OperationCanceledException => "Timed out connecting to the camera.",
        SocketException socket => socket.Message,
        _ => ex.Message,
    };

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
            _logger.LogWarning(ex, "RTSP client for {name} stopped with an error.", Name());
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
            var configVersion = Volatile.Read(ref _configVersion);
            TimeSpan retryDelay;

            try
            {
                await RunSessionAsync(ct);
                // A session that ended because the settings moved has nothing to back off
                // from - reconnecting at once is what makes an edit land on a camera that
                // was running.
                retryDelay = Volatile.Read(ref _configVersion) != configVersion
                    ? TimeSpan.Zero
                    : NextRetryDelay();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The warning is logged on every attempt; only the state the UI reads waits
                // for the failures to pile up. Frames clear both.
                if (Interlocked.Increment(ref _consecutiveFailures) >= ErrorAfterFailures)
                    SetError(DescribeFailure(ex));

                retryDelay = NextRetryDelay();
                _logger.LogWarning(ex, "RTSP session for {name} failed - reconnecting in {seconds:0}s.",
                    Name(), retryDelay.TotalSeconds);
            }

            if (retryDelay <= TimeSpan.Zero)
                continue;

            try
            {
                await Task.Delay(retryDelay, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // Failed sessions get further apart instead of a flat 5s: a camera that turns connections
    // away is only pushed further by the retries themselves, because firmware that is slow to
    // release a dropped session keeps accumulating the ones being opened. Frames reset this.
    private TimeSpan NextRetryDelay()
    {
        var attempt = Interlocked.Increment(ref _retryAttempt);
        var seconds = RetryMinDelay.TotalSeconds * Math.Pow(2, attempt - 1);
        return TimeSpan.FromSeconds(Math.Min(seconds, RetryMaxDelay.TotalSeconds));
    }

    private async Task RunSessionAsync(CancellationToken ct)
    {
        // Which settings this session was built from, so the wait loop below can tell that
        // they moved and hand the camera to a session built from the new ones.
        var configVersion = Volatile.Read(ref _configVersion);

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

            _logger.LogInformation("PLAY OK - receiving stream for camera {name}.", Name());

            lock (_stateGate)
            {
                // The new session gets its own clock: silence is measured from here, not from
                // whatever the session that ended left behind.
                _lastFrameAt = null;
                _sessionLive = true;
            }

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(ct))
            {
                _stats.LogAndReset(_logger);

                // An edited camera still points at the old address or credentials here, and
                // nothing in the socket would ever say so - the session has to be rebuilt
                // from the new settings instead.
                if (Volatile.Read(ref _configVersion) != configVersion)
                {
                    _logger.LogInformation("Config changed for camera {name} - reconnecting.", Name());
                    return;
                }

                if (_transport.Connected)
                    continue;

                _logger.LogWarning("RTSP connection lost - closing session (client will reconnect).");
                return;
            }
        }
        finally
        {
            lock (_stateGate)
            {
                _sessionLive = false;
            }

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
        string configUrl;
        string? configuredCredentials;
        lock (_configGate)
        {
            configUrl = _configUrl;
            configuredCredentials = _configuredCredentials;
        }

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
                    request.RequestTyped, Name());
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
        // Frames mean the camera answered, so any later reconnect starts from the short delay
        // and any failure run counted so far is over.
        Interlocked.Exchange(ref _retryAttempt, 0);
        Interlocked.Exchange(ref _consecutiveFailures, 0);

        _stats.CountFrame(frame);

        lock (_stateGate)
        {
            _lastFrameAt = DateTimeOffset.UtcNow;
            _lastError = null;
        }

        FrameReceived?.Invoke(frame);
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
                    logger.LogInformation("No RTP packets (channel 0) received in {seconds:0.0}s.", seconds);

                _packets = _rtpBytes = _frames = _idr = _sps = _pps = 0;
                _clock.Restart();
            }
        }
    }
}
