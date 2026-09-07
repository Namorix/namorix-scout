using Microsoft.EntityFrameworkCore;
using Namorix.Scout.Dtos;
using Namorix.Scout.Models;
using Namorix.Scout.Persistence;

namespace Namorix.Scout.Services;

public sealed class CameraService(
    IDbContextFactory<ScoutDbContext> dbFactory,
    ScoutSecretProtector secretProtector)
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "rtsp",
        "rtsps",
    };

    public async Task<ScCameraDto[]> ListAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cameras = await db.Cameras.AsNoTracking().ToListAsync(ct);
        return cameras.OrderBy(c => c.CreatedAt).Select(ToDto).ToArray();
    }

    public async Task<ScCameraDto?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var camera = await db.Cameras.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id, ct);
        return camera is null ? null : ToDto(camera);
    }

    public async Task<ScCameraDto> CreateAsync(CameraUpsertRequest request, CancellationToken ct)
    {
        var camera = new ScCamera
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Apply(camera, request);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Cameras.Add(camera);
        await db.SaveChangesAsync(ct);
        return ToDto(camera);
    }

    public async Task<ScCameraDto?> UpdateAsync(Guid id, CameraUpsertRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var camera = await db.Cameras.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (camera is null) return null;

        Apply(camera, request);
        await db.SaveChangesAsync(ct);
        return ToDto(camera);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var camera = await db.Cameras.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (camera is null) return false;

        db.Cameras.Remove(camera);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private void Apply(ScCamera camera, CameraUpsertRequest request)
    {
        camera.Name = RequireName(request.Name);
        var (sanitizedUrl, urlCredentials) = SplitRtspUrl(request.RtspUrl);
        camera.RtspUrl = sanitizedUrl;
        camera.StreamType = ParseStreamType(request.StreamType);
        camera.Enabled = request.Enabled ?? true;
        camera.RecordEnabled = request.RecordEnabled ?? false;
        camera.RetentionDays = ParseRetentionDays(request.RetentionDays);
        var credentials = ResolveCredentials(request.Username, request.Password, urlCredentials);
        if (credentials is not null)
            camera.RtspCredentials = secretProtector.Protect(credentials);
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

    private ScCameraDto ToDto(ScCamera camera)
    {
        var username = ReadUsername(camera.RtspCredentials);
        return new ScCameraDto(
            camera.Id,
            camera.Name,
            camera.RtspUrl,
            camera.StreamType.ToString().ToLowerInvariant(),
            camera.Enabled,
            camera.RecordEnabled,
            camera.RetentionDays,
            !string.IsNullOrEmpty(camera.RtspCredentials),
            username,
            camera.CreatedAt);
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
