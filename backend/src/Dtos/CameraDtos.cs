namespace Namorix.Scout.Dtos;

// One shape for both roles, with the owner-only half nullable rather than a second record: a
// shared list mixes owned and shared cameras, and two records would force the caller to merge
// two payload shapes. Null means "not yours to know", and the serializer omits nulls, so a
// viewer's payload carries no rtspUrl/username/hasCredentials keys at all.
public sealed record ScCameraDto(
    Guid Id,
    string Name,
    string? RtspUrl,
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
    DateTimeOffset? LastFrameAt,
    string Access);

// Owner-only view of who else can see the camera. The name is filled in from the desktop on a
// best-effort basis: the grant is the addon's own row and outlives the desktop being up, so an
// unreachable desktop leaves the name null instead of failing the request.
public sealed record CameraShareDto(
    int UserId,
    string? Username,
    string? Name,
    string Permission,
    DateTimeOffset CreatedAt);

public sealed record CameraShareRequest(int UserId, string? Permission);

public sealed record CameraUpsertRequest(
    string? Name,
    string? RtspUrl,
    string? StreamType,
    bool? Enabled,
    bool? RecordEnabled,
    int? RetentionDays,
    string? Username,
    string? Password);
