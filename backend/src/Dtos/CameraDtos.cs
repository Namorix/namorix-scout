namespace Namorix.Scout.Dtos;

public sealed record ScCameraDto(
    Guid Id,
    string Name,
    string RtspUrl,
    string StreamType,
    bool Enabled,
    bool RecordEnabled,
    int RetentionDays,
    bool HasCredentials,
    string? Username,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdatedAt,
    string State,
    string? LastError,
    DateTimeOffset? LastFrameAt);

public sealed record CameraUpsertRequest(
    string? Name,
    string? RtspUrl,
    string? StreamType,
    bool? Enabled,
    bool? RecordEnabled,
    int? RetentionDays,
    string? Username,
    string? Password);
