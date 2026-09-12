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

    public async Task<RtcOffer?> CreateAsync(Guid cameraId, CancellationToken ct)
    {
        var camera = ingest.GetActiveClient(cameraId);
        if (camera is null)
            return null;

        var session = new RtcViewerSession(camera, logger);
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
}

internal sealed class RtcViewerSession(CameraRtspClient camera, ILogger logger)
{
    private readonly Lock _gate = new();
    private readonly ConcurrentQueue<string> _ice = new();
    private readonly List<byte[]> _packets = [];
    private readonly Regex _h264Rtpmap =
        new(@"a=rtpmap:(\d+)\s+H264/90000", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private RTCPeerConnection? _peer;
    private bool _connected;
    private int _closedFlag;

    public Guid Id { get; } = Guid.NewGuid();
    public int PayloadType { get; private set; } = 96;

    public event Action? Closed;

    public async Task<string> StartOfferAsync(CancellationToken ct)
    {
        var peer = new RTCPeerConnection(null);
        _peer = peer;

        var codec = camera.GetCodec();
        var fmtp = codec is null
            ? "packetization-mode=1;profile-level-id=42e01f"
            : $"packetization-mode=1;profile-level-id={codec.ProfileLevelId}";

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
        switch (state)
        {
            case RTCPeerConnectionState.connected:
            {
                bool alreadyConnected;
                lock (_gate)
                {
                    alreadyConnected = _connected;
                    _connected = true;
                }

                if (alreadyConnected)
                    return;
                
                camera.FrameReceived += OnFrame;
                logger.LogInformation("WebRTC session {sessionId}: connected, relaying camera frames.", Id);

                return;
            }
            case RTCPeerConnectionState.failed:
            case RTCPeerConnectionState.closed:
            case RTCPeerConnectionState.disconnected:
                logger.LogInformation("WebRTC session {sessionId}: connection state {state}.", Id, state);
                _ = CloseAsync();
                break;

            case RTCPeerConnectionState.@new:
            case RTCPeerConnectionState.connecting:
                break;
            
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
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
        var peer = _peer;
        if (peer is null)
            return;

        lock (_gate)
        {
            if (!_connected)
                return;
        }

        try
        {
            _packets.Clear();
            foreach (var nal in frame.Nals)
                H264RtpPacketizer.PacketizeNal(nal, H264RtpPacketizer.MaxPayloadBytes, _packets);

            for (var i = 0; i < _packets.Count; i++)
            {
                var marker = i == _packets.Count - 1 ? 1 : 0;
                peer.SendRtpRaw(SDPMediaTypesEnum.video, _packets[i], frame.RtpTimestamp, marker, PayloadType);
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
