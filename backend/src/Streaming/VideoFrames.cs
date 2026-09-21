namespace Namorix.Scout.Streaming;

public sealed class VideoFrame
{
    public required uint RtpTimestamp { get; init; }
    public required bool IsKeyFrame { get; init; }
    public required IReadOnlyList<byte[]> Nals { get; init; }
    public required int Bytes { get; init; }
}
