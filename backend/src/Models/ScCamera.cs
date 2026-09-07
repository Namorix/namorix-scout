namespace Namorix.Scout.Models;

public sealed class ScCamera
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RtspUrl { get; set; } = string.Empty;
    public string? RtspCredentials { get; set; }
    public CameraStreamType StreamType { get; set; } = CameraStreamType.Main;
    public bool Enabled { get; set; } = true;
    public bool RecordEnabled { get; set; }
    public int RetentionDays { get; set; } = 7;
    public DateTimeOffset CreatedAt { get; set; }
}
