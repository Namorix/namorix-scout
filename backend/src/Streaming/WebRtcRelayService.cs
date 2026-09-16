using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace Namorix.Scout.Streaming;

public sealed class RtcOffer
{
    public Guid SessionId { get; init; }
    public string Sdp { get; init; } = "";
    public int PayloadType { get; init; }
}

public sealed class WebRtcRelayService(
    RtspIngestService ingest,
    ILogger<WebRtcRelayService> logger)
{
    private readonly ConcurrentDictionary<Guid, RtcViewerSession> _sessions = new();

    public async Task<RtcOffer?> CreateAsync(Guid cameraId, int userId, CancellationToken ct)
    {
        var camera = ingest.GetActiveClient(cameraId);
        if (camera is null)
            return null;

        var session = new RtcViewerSession(camera, userId, logger);
        string sdp;
        try
        {
            sdp = await session.StartOfferAsync(ct);
        }
        catch
        {
            await session.CloseAsync();
            throw;
        }

        if (!_sessions.TryAdd(session.Id, session))
        {
            await session.CloseAsync();
            return null;
        }

        session.Closed += () => _sessions.TryRemove(session.Id, out _);
        logger.LogInformation("WebRTC offer ready for camera {cameraId}: session {sessionId}.", cameraId, session.Id);
        return new RtcOffer { SessionId = session.Id, Sdp = sdp, PayloadType = session.PayloadType };
    }

    internal RtcViewerSession? Find(Guid sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session : null;

    public bool SetRemoteAnswer(Guid sessionId, string? sdp)
    {
        var session = Find(sessionId);
        return session is not null && session.ApplyAnswer(sdp);
    }

    public void AddRemoteIce(Guid sessionId, string? candidate)
    {
        if (Find(sessionId) is { } session)
            session.AddRemoteIce(candidate);
    }

    public IReadOnlyList<string> DrainIce(Guid sessionId) =>
        Find(sessionId)?.DrainIce() ?? [];

    public async Task StopAsync(Guid sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
            await session.CloseAsync();
    }

    // Revoking a share has to end the session that share was granting: the viewer's peer is
    // already carrying the stream and would otherwise keep playing until it left on its own.
    // The browser reconnects by itself, and that next offer is refused because access is
    // decided from the share table, which no longer names this user.
    public async Task CloseViewerSessionsAsync(Guid cameraId, int userId)
    {
        var doomed = _sessions
            .Where(entry => entry.Value.CameraId == cameraId && entry.Value.UserId == userId)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var sessionId in doomed)
            await StopAsync(sessionId);

        if (doomed.Count > 0)
            logger.LogInformation(
                "Access revoked: closed {count} live session(s) for user {userId} on camera {cameraId}.",
                doomed.Count, userId, cameraId);
    }
}

internal sealed class RtcViewerSession(CameraRtspClient camera, int userId, ILogger logger)
{
    private const int DisconnectGraceSeconds = 15;

    private readonly Lock _gate = new();
    private readonly ConcurrentQueue<string> _ice = new();
    private readonly List<byte[]> _packets = [];
    private readonly Regex _h264Rtpmap =
        new(@"a=rtpmap:(\d+)\s+H264/90000", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private RTCPeerConnection? _peer;
    private bool _connected;
    private int _closedFlag;
    private int _disconnectEpoch;
    private VideoFrame? _lastKeyFrame;
    private uint _lastRtpTimestamp;

    public Guid Id { get; } = Guid.NewGuid();
    public Guid CameraId => camera.CameraId;
    // Held so a revoke can pick out exactly the sessions it takes away; the relay is otherwise
    // addressed by session id alone.
    public int UserId { get; } = userId;
    public int PayloadType { get; private set; } = 96;

    public event Action? Closed;

    public async Task<string> StartOfferAsync(CancellationToken ct)
    {
        var peer = new RTCPeerConnection(null);
        _peer = peer;

        var codec = camera.GetCodec();
        // The browser weighs the advertised profile-level-id against the H264 profiles it
        // ships and silently drops the payload when it does not know the one advertised,
        // which leaves an offer the browser answers with the video section rejected. So
        // advertise the profile every browser has instead of the camera's own: the real
        // parameter sets still reach the decoder in-band, via ReplayKeyFrame.
        var fmtp = "packetization-mode=1;profile-level-id=42e01f";

        logger.LogInformation(
            "WebRTC session {sessionId}: offering H264 {fmtp} for camera {cameraId} (camera SPS profile-level-id {profile}).",
            Id, fmtp, camera.CameraId, codec?.ProfileLevelId ?? "(none)");

        var track = new MediaStreamTrack(
            new List<VideoFormat>
            {
                new(VideoCodecsEnum.H264, 96, 90_000, fmtp),
            },
            MediaStreamStatusEnum.SendOnly);
        peer.addTrack(track);

        peer.onicecandidate += OnIceCandidate;
        peer.onconnectionstatechange += OnConnectionStateChange;

        var offer = peer.createOffer();
        var match = _h264Rtpmap.Match(offer.sdp);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var payloadType))
            PayloadType = payloadType;

        await peer.setLocalDescription(offer);

        // Subscribe before the connection is up so the most recent IDR is already
        // cached by the time the viewer connects.
        camera.FrameReceived += OnFrame;

        ct.ThrowIfCancellationRequested();
        return offer.sdp;
    }

    private void OnIceCandidate(RTCIceCandidate? candidate)
    {
        if (candidate is null)
            return;

        var line = candidate.ToString();
        if (!string.IsNullOrWhiteSpace(line))
            _ice.Enqueue(line.Trim());
    }

    private void OnConnectionStateChange(RTCPeerConnectionState state)
    {
        if (Volatile.Read(ref _closedFlag) != 0)
            return;

        switch (state)
        {
            case RTCPeerConnectionState.connected:
            {
                Interlocked.Increment(ref _disconnectEpoch);

                bool alreadyConnected;
                lock (_gate)
                {
                    alreadyConnected = _connected;
                    _connected = true;
                }

                if (alreadyConnected)
                    return;

                logger.LogInformation("WebRTC session {sessionId}: connected, relaying camera frames.", Id);
                ReplayKeyFrame();

                return;
            }
            case RTCPeerConnectionState.disconnected:
                // ICE consent freshness fails transiently when the viewer is throttled
                // (background tab) and usually recovers on its own - only tear the
                // session down if it is still down after the grace window.
                logger.LogInformation("WebRTC session {sessionId}: disconnected, waiting {seconds}s for recovery.",
                    Id, DisconnectGraceSeconds);
                StartDisconnectGrace();
                return;

            case RTCPeerConnectionState.failed:
            case RTCPeerConnectionState.closed:
                logger.LogInformation("WebRTC session {sessionId}: connection state {state}.", Id, state);
                _ = CloseAsync();
                return;

            case RTCPeerConnectionState.@new:
            case RTCPeerConnectionState.connecting:
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    private void StartDisconnectGrace()
    {
        var epoch = Interlocked.Increment(ref _disconnectEpoch);
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(DisconnectGraceSeconds));
            if (Volatile.Read(ref _disconnectEpoch) != epoch || Volatile.Read(ref _closedFlag) != 0)
                return;

            logger.LogInformation("WebRTC session {sessionId}: still disconnected after grace, closing.", Id);
            await CloseAsync();
        });
    }

    public bool ApplyAnswer(string? sdp)
    {
        var peer = _peer;
        if (peer is null || string.IsNullOrWhiteSpace(sdp))
            return false;

        var result = peer.setRemoteDescription(new RTCSessionDescriptionInit
        {
            sdp = sdp,
            type = RTCSdpType.answer,
        });
        // The browser builds this answer from our offer, so a rejection here is the one place
        // that says why a viewer never connects.
        if (result != SetDescriptionResultEnum.OK)
            logger.LogWarning("WebRTC session {sessionId}: answer rejected with {result}.", Id, result);

        return result == SetDescriptionResultEnum.OK;
    }

    public void AddRemoteIce(string? candidate)
    {
        var peer = _peer;
        if (peer is null || string.IsNullOrWhiteSpace(candidate))
            return;

        try
        {
            peer.addIceCandidate(new RTCIceCandidateInit { candidate = candidate });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to add remote ICE candidate for session {sessionId}.", Id);
        }
    }

    public IReadOnlyList<string> DrainIce()
    {
        var lines = new List<string>();
        while (_ice.TryDequeue(out var line))
            lines.Add(line);
        return lines;
    }

    private void OnFrame(VideoFrame frame)
    {
        _lastRtpTimestamp = frame.RtpTimestamp;
        if (frame.IsKeyFrame)
            _lastKeyFrame = frame;

        var peer = _peer;
        if (peer is null)
            return;

        lock (_gate)
        {
            if (!_connected)
                return;

            SendFrame(peer, frame.Nals, frame.RtpTimestamp);
        }
    }

    // A fresh viewer has no reference frames, so it stays black until the camera
    // emits its next IDR (up to one GOP). Replaying the cached IDR paints it now.
    private void ReplayKeyFrame()
    {
        var frame = _lastKeyFrame;
        var peer = _peer;
        if (frame is null || peer is null)
            return;

        var nals = new List<byte[]>(frame.Nals.Count + 2);
        var codec = camera.GetCodec();
        if (codec is not null)
        {
            if (!frame.Nals.Any(nal => nal.Length > 0 && (nal[0] & 0x1F) == 7))
                nals.Add(codec.Sps);
            if (!frame.Nals.Any(nal => nal.Length > 0 && (nal[0] & 0x1F) == 8))
                nals.Add(codec.Pps);
        }

        nals.AddRange(frame.Nals);

        lock (_gate)
        {
            if (!_connected)
                return;

            SendFrame(peer, nals, _lastRtpTimestamp + 1);
        }
    }

    private void SendFrame(RTCPeerConnection peer, IReadOnlyList<byte[]> nals, uint rtpTimestamp)
    {
        try
        {
            _packets.Clear();
            foreach (var nal in nals)
                H264RtpPacketizer.PacketizeNal(nal, H264RtpPacketizer.MaxPayloadBytes, _packets);

            for (var i = 0; i < _packets.Count; i++)
            {
                var marker = i == _packets.Count - 1 ? 1 : 0;
                peer.SendRtpRaw(SDPMediaTypesEnum.video, _packets[i], rtpTimestamp, marker, PayloadType);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error sending WebRTC video for session {sessionId}.", Id);
        }
    }

    public Task CloseAsync()
    {
        if (Interlocked.Exchange(ref _closedFlag, 1) != 0)
            return Task.CompletedTask;

        lock (_gate)
        {
            _connected = false;
        }

        camera.FrameReceived -= OnFrame;
        Closed?.Invoke();

        var peer = _peer;
        _peer = null;
        if (peer is null)
            return Task.CompletedTask;

        try
        {
            peer.close();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error closing peer for session {sessionId}.", Id);
        }

        try
        {
            peer.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error disposing peer for session {sessionId}.", Id);
        }

        return Task.CompletedTask;
    }
}
