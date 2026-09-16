using Microsoft.EntityFrameworkCore;
using Namorix.Core.Grpc;
using Namorix.Core.Protos;
using Namorix.Scout.Dtos;
using Namorix.Scout.Models;
using Namorix.Scout.Persistence;
using Namorix.Scout.Streaming;

namespace Namorix.Scout.Services;

public sealed class CameraService(
    IDbContextFactory<ScoutDbContext> dbFactory,
    ScoutSecretProtector secretProtector,
    CameraChangeSignal changes,
    RtspIngestService ingest,
    AddonChannelClient channel,
    ILogger<CameraService> logger)
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "rtsp",
        "rtsps",
    };

    public async Task<ScCameraDto[]> ListAsync(int userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Access is settled inside the query rather than by a lookup per row: the caller's share
        // is projected alongside the camera, and a missing share means the caller owns it.
        var rows = await db.Cameras.AsNoTracking()
            .Where(c => c.UserId == userId
                || db.CameraShares.Any(s => s.CameraId == c.Id && s.UserId == userId))
            .Select(c => new
            {
                Camera = c,
                Permission = db.CameraShares
                    .Where(s => s.CameraId == c.Id && s.UserId == userId)
                    .Select(s => (CameraSharePermission?)s.Permission)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows.OrderBy(r => r.Camera.CreatedAt)
            .Select(r => ToDto(r.Camera, AccessFor(r.Camera.UserId == userId, r.Permission)))
            .ToArray();
    }

    public async Task<ScCameraDto?> GetAsync(Guid id, int userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var camera = await db.Cameras.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (camera is null) return null;

        var access = camera.UserId == userId
            ? CameraAccess.Owner
            : await ShareAccessAsync(db, id, userId, ct);
        return access is null ? null : ToDto(camera, access.Value);
    }

    public async Task<ScCameraDto> CreateAsync(int userId, CameraUpsertRequest request, CancellationToken ct)
    {
        var camera = new ScCamera
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Apply(camera, request, CameraAccess.Owner);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Cameras.Add(camera);
        await db.SaveChangesAsync(ct);
        changes.Notify();
        return ToDto(camera, CameraAccess.Owner);
    }

    public async Task<ScCameraDto?> UpdateAsync(Guid id, int userId, CameraUpsertRequest request,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var camera = await db.Cameras.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (camera is null) return null;

        var access = camera.UserId == userId
            ? CameraAccess.Owner
            : await ShareAccessAsync(db, id, userId, ct);
        if (access is not (CameraAccess.Owner or CameraAccess.Manage)) return null;

        // A manage share may change what the camera does, never where it points: re-pointing
        // the address or the account would hand the owner's stream to someone else's server.
        if (access is not CameraAccess.Owner && SendsConnection(request))
            throw new ArgumentException(
                "Only the owner can change the camera address, stream type or credentials.");

        Apply(camera, request, access.Value);
        await db.SaveChangesAsync(ct);
        changes.Notify();
        return ToDto(camera, access.Value);
    }

    public async Task<bool> DeleteAsync(Guid id, int userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var camera = await db.Cameras.SingleOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
        if (camera is null) return false;

        db.Cameras.Remove(camera);
        await db.SaveChangesAsync(ct);
        changes.Notify();
        return true;
    }

    public async Task<CameraShareDto[]?> ListSharesAsync(Guid id, int userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Cameras.AnyAsync(c => c.Id == id && c.UserId == userId, ct))
            return null;

        // Sorted after the query rather than in it: CreatedAt is a DateTimeOffset, which SQLite
        // refuses to ORDER BY. Same shape as the camera list above.
        var shares = await db.CameraShares.AsNoTracking()
            .Where(s => s.CameraId == id)
            .ToListAsync(ct);

        var names = await ResolveNamesAsync(shares.Select(s => s.UserId), ct);
        return shares.OrderBy(s => s.CreatedAt)
            .Select(s => ToShareDto(s, names))
            .ToArray();
    }

    public async Task<CameraShareDto?> AddShareAsync(Guid id, int userId, CameraShareRequest request,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Cameras.AnyAsync(c => c.Id == id && c.UserId == userId, ct))
            return null;

        if (request.UserId == userId)
            throw new ArgumentException("The owner already has access to this camera.");

        // Re-sharing catches the pair rather than failing on the primary key: granting a level
        // someone already holds is a no-op, and changing it should not need a delete first.
        var share = await db.CameraShares
            .SingleOrDefaultAsync(s => s.CameraId == id && s.UserId == request.UserId, ct);
        if (share is null)
        {
            share = new CameraShare
            {
                CameraId = id,
                UserId = request.UserId,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.CameraShares.Add(share);
        }

        share.Permission = ParsePermission(request.Permission);
        await db.SaveChangesAsync(ct);
        changes.Notify();

        var names = await ResolveNamesAsync([request.UserId], ct);
        return ToShareDto(share, names);
    }

    public async Task<bool> RemoveShareAsync(Guid id, int userId, int targetUserId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Cameras.AnyAsync(c => c.Id == id && c.UserId == userId, ct))
            return false;

        var share = await db.CameraShares
            .SingleOrDefaultAsync(s => s.CameraId == id && s.UserId == targetUserId, ct);
        if (share is null) return true;

        db.CameraShares.Remove(share);
        await db.SaveChangesAsync(ct);
        changes.Notify();
        return true;
    }

    private static async Task<CameraAccess?> ShareAccessAsync(
        ScoutDbContext db, Guid cameraId, int userId, CancellationToken ct)
    {
        var permission = await db.CameraShares.AsNoTracking()
            .Where(s => s.CameraId == cameraId && s.UserId == userId)
            .Select(s => (CameraSharePermission?)s.Permission)
            .SingleOrDefaultAsync(ct);

        return permission switch
        {
            CameraSharePermission.Manage => CameraAccess.Manage,
            CameraSharePermission.View => CameraAccess.View,
            _ => null,
        };
    }

    private static CameraAccess AccessFor(bool owned, CameraSharePermission? permission) =>
        owned ? CameraAccess.Owner
        : permission is CameraSharePermission.Manage ? CameraAccess.Manage
        : CameraAccess.View;

    // Presence is what matters, not the value: a non-owner sending any of these is asking for
    // something the share cannot give, whether or not it differs from what is stored.
    private static bool SendsConnection(CameraUpsertRequest request) =>
        request.RtspUrl is not null || request.Username is not null
        || request.Password is not null || request.StreamType is not null;

    private static CameraSharePermission ParsePermission(string? permission) =>
        string.IsNullOrWhiteSpace(permission)
            ? CameraSharePermission.View
            : permission.ToLowerInvariant() switch
            {
                "view" => CameraSharePermission.View,
                "manage" => CameraSharePermission.Manage,
                _ => throw new ArgumentException("Permission must be 'view' or 'manage'."),
            };

    // Names come from the desktop, which owns the user table. A grant outlives the desktop
    // being reachable, and the owner still has to see and revoke it, so an unreachable desktop
    // leaves the names null rather than failing the whole list.
    private async Task<Dictionary<int, AddonUser>> ResolveNamesAsync(
        IEnumerable<int> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        try
        {
            var response = await channel.GetUsersAsync(ids, ct);
            return response.Users.ToDictionary(u => (int)u.UserId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not resolve {Count} user id(s) for a camera share list",
                ids.Count);
            return [];
        }
    }

    private static CameraShareDto ToShareDto(CameraShare share, Dictionary<int, AddonUser> names) =>
        names.TryGetValue(share.UserId, out var user)
            ? new CameraShareDto(share.UserId, user.Username, user.Name,
                share.Permission.ToString().ToLowerInvariant(), share.CreatedAt)
            : new CameraShareDto(share.UserId, null, null,
                share.Permission.ToString().ToLowerInvariant(), share.CreatedAt);

    private void Apply(ScCamera camera, CameraUpsertRequest request, CameraAccess access)
    {
        camera.Name = RequireName(request.Name);

        if (access is CameraAccess.Owner)
        {
            var (sanitizedUrl, urlCredentials) = SplitRtspUrl(request.RtspUrl);
            camera.RtspUrl = sanitizedUrl;
            camera.StreamType = ParseStreamType(request.StreamType);
            var credentials = ResolveCredentials(request.Username, request.Password, urlCredentials);
            if (credentials is not null)
                camera.RtspCredentials = secretProtector.Protect(credentials);
        }

        camera.Enabled = request.Enabled ?? true;
        camera.RecordEnabled = request.RecordEnabled ?? false;
        camera.RetentionDays = ParseRetentionDays(request.RetentionDays);
        camera.LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string RequireName(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Camera name is required.")
            : name.Trim();

    private static (string Sanitized, string? Credentials) SplitRtspUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !AllowedSchemes.Contains(uri.Scheme))
        {
            throw new ArgumentException("A valid rtsp:// or rtsps:// URL is required.");
        }

        var schemeSeparator = url.IndexOf("://", StringComparison.Ordinal);
        var authorityStart = schemeSeparator + 3;
        var authorityEnd = url.IndexOf('/', authorityStart);
        if (authorityEnd < 0) authorityEnd = url.Length;

        var authority = url[authorityStart..authorityEnd];
        var at = authority.LastIndexOf('@');

        return at <= 0 ? (url, null) :
            (url.Remove(authorityStart, at + 1), authority[..at]);
    }

    private static string? ResolveCredentials(string? username, string? password, string? urlCredentials)
    {
        var user = string.IsNullOrWhiteSpace(username) ? null : username.Trim();
        if (string.IsNullOrEmpty(password))
            return urlCredentials;
        if (user is null)
            throw new ArgumentException("Username is required when a password is set.");
        return $"{user}:{password}";
    }

    private static CameraStreamType ParseStreamType(string? streamType)
    {
        if (string.IsNullOrWhiteSpace(streamType) ||
            streamType.Equals("main", StringComparison.OrdinalIgnoreCase))
        {
            return CameraStreamType.Main;
        }
        
        return streamType.Equals("sub", StringComparison.OrdinalIgnoreCase) ? CameraStreamType.Sub :
            throw new ArgumentException("StreamType must be 'main' or 'sub'.");
    }

    private static int ParseRetentionDays(int? retentionDays)
    {
        var days = retentionDays ?? 7;
        return days is < 1 or > 365 ? throw new ArgumentException("RetentionDays must be between 1 and 365.") : days;
    }

    private ScCameraDto ToDto(ScCamera camera, CameraAccess access)
    {
        var owner = access is CameraAccess.Owner;
        // Merged in here rather than in the controller so every list/get/create/update
        // answer carries the same live health, with no second request for the UI.
        var status = ingest.GetStatus(camera.Id);
        return new ScCameraDto(
            camera.Id,
            camera.Name,
            owner ? camera.RtspUrl : null,
            camera.StreamType.ToString().ToLowerInvariant(),
            camera.Enabled,
            camera.RecordEnabled,
            camera.RetentionDays,
            owner && !string.IsNullOrEmpty(camera.RtspCredentials),
            owner ? ReadUsername(camera.RtspCredentials) : null,
            camera.CreatedAt,
            camera.LastUpdatedAt,
            status.State.ToString().ToLowerInvariant(),
            // An RTSP failure usually names the host it could not reach, so the reason the
            // owner sees is withheld from everyone else along with the rest of the address.
            owner ? status.LastError : null,
            status.LastFrameAt,
            access.ToString().ToLowerInvariant());
    }

    private string? ReadUsername(string? protectedCredentials)
    {
        if (string.IsNullOrEmpty(protectedCredentials))
            return null;

        string? plain;
        try
        {
            plain = secretProtector.Unprotect(protectedCredentials);
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrEmpty(plain))
            return null;

        var colon = plain.IndexOf(':');
        return colon >= 0 ? plain[..colon] : plain;
    }
}
