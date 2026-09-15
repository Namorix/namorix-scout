namespace Namorix.Scout.Models;

public enum CameraStreamType
{
    Main = 0,
    Sub = 1,
}

// Health of the running ingest client. It describes the process rather than the row, so it
// is read from memory on every request and never persisted.
public enum CameraRuntimeState
{
    Stopped = 0,
    Connecting = 1,
    Streaming = 2,
    Failed = 3,
}

public sealed record CameraRuntimeStatus(
    CameraRuntimeState State,
    string? LastError,
    DateTimeOffset? LastFrameAt);
