namespace Namorix.Scout.Models;

public enum CameraStreamType
{
    Main = 0,
    Sub = 1,
}

// What a share lets the other user do. Two levels only, because there is no second axis to
// split on yet: recording and playback do not exist, so inventing a finer scale now would be
// designing for an absent feature.
public enum CameraSharePermission
{
    View = 0,
    Manage = 1,
}

// How the caller reaches a camera. Owner is not a share level — it is the absence of one — but
// all three answer the same question, so the API answers them on a single field the UI branches on.
public enum CameraAccess
{
    Owner = 0,
    Manage = 1,
    View = 2,
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
