namespace Namorix.Scout.Streaming;

public sealed class VideoFrame
{
    public required uint RtpTimestamp { get; init; }
    public required bool IsKeyFrame { get; init; }
    public required IReadOnlyList<byte[]> Nals { get; init; }
    public required int Bytes { get; init; }
}

public sealed class H264CodecSnapshot
{
    public required byte[] Sps { get; init; }
    public required byte[] Pps { get; init; }
    public required string ProfileLevelId { get; init; }
    public required string SpropParameterSets { get; init; }
}
