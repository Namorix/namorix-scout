using SharpMP4.Builders;
using SharpMP4.Tracks;

namespace Namorix.Scout.Streaming;

public sealed record HlsSegment(int Sequence, byte[] Data);

// One packager per camera, shared by every viewer: muxing is per-stream work, so N viewers
// reading the same segments costs no more than one.
public sealed class HlsPackager : IDisposable
{
    // SharpMP4 cuts fragments on elapsed time, not on keyframes, and a segment is only
    // decodable when it starts on one - so this has to match the camera's I-frame interval.
    // 2s is what most cameras ship with, and a mismatch is reported below at runtime.
    public const int SegmentMilliseconds = 2_000;

    // ~16s of video: long enough for a slow segment fetch, short enough that a viewer joining
    // a long-running stream does not buffer through the whole backlog.
    public const int RetainedSegments = 8;

    private readonly ILogger<HlsPackager> _logger;
    private readonly Lock _gate = new();

    private readonly FragmentedBlobOutput _output;
    private readonly H264Track _track;
    private readonly FragmentedMp4Builder _builder;
    private readonly Queue<HlsSegment> _segments = new();

    private byte[]? _init;
    private int _lastSequence;
    private bool _misalignedReported;
    private bool _parseFailureReported;
    private bool _disposed;

    public HlsPackager(ILogger<HlsPackager> logger)
    {
        _logger = logger;
        _output = new FragmentedBlobOutput();
        _output.OnFragmentReady += OnFragmentReady;
        // Duration 0 marks a live source and the random access box only means anything for a
        // file that ends - both wrong for a stream that never does.
        _builder = new FragmentedMp4Builder(_output, SegmentMilliseconds, 0, false);
        // Bare ctor on purpose: the track reads the frame rate out of the camera's own SPS
        // VUI, which is the only place a 15fps camera and a 25fps one can be told apart.
        _track = new H264Track();
        _builder.AddTrack(_track);
    }

    public byte[]? InitSegment
    {
        get
        {
            lock (_gate) return _init;
        }
    }

    public IReadOnlyList<HlsSegment> Segments
    {
        get
        {
            lock (_gate) return _segments.ToArray();
        }
    }

    // Fed one access unit at a time, straight off CameraRtspClient.FrameReceived. The packager
    // does not subscribe itself: the registry owns the lifetime, including rebuilding the
    // packager when the ingest client is replaced.
    public void PushFrame(VideoFrame frame)
    {
        lock (_gate)
        {
            if (_disposed) return;

            var before = _lastSequence;
            try
            {
                // One NAL at a time: the track groups them into access units itself, off the
                // slice headers, so SPS/PPS arriving in their own frame still open one.
                foreach (var nal in frame.Nals)
                    _builder.ProcessTrackSample(_track.TrackID, nal, -1);
            }
            catch (Exception ex)
            {
                // We are called from the RTSP receive loop, so an unparseable NAL - and the
                // parser throws on unknown slice types - would otherwise take the camera's
                // whole ingest down, WebRTC included. The stream recovers at the next keyframe.
                if (!_parseFailureReported)
                {
                    _parseFailureReported = true;
                    _logger.LogWarning(ex, "HLS packager dropped a frame it could not parse.");
                }

                return;
            }

            // A boundary landing mid-GOP is the failure nothing downstream would report: the
            // segment is emitted, the playlist serves it, and the player just shows nothing.
            if (_lastSequence != before && !frame.IsKeyFrame && !_misalignedReported)
            {
                _misalignedReported = true;
                _logger.LogWarning(
                    "HLS segment started on a non-keyframe. "
                    + "Set SegmentMilliseconds to this camera's I-frame interval.");
            }
        }
    }

    private void OnFragmentReady(object? sender, FragmentBlobEventArgs e)
    {
        // Sequence 0 is the initialization segment: every media segment needs it in front.
        if (e.SequenceNumber == 0)
        {
            _init = e.Data;
            return;
        }

        _lastSequence = e.SequenceNumber;
        _segments.Enqueue(new HlsSegment(e.SequenceNumber, e.Data));
        while (_segments.Count > RetainedSegments)
            _segments.Dequeue();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _output.OnFragmentReady -= OnFragmentReady;
            _output.Dispose();
        }
    }
}
