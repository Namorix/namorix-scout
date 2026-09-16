namespace Namorix.Scout.Models;

// Grants one user access to a camera they do not own. Sharing rather than letting each user
// add the same address on their own keeps a single camera row, and therefore a single RTSP
// pull: the ingest and the relay are both keyed by camera id, not by URL. It also settles the
// question a duplicate-row design cannot answer — whose credentials the stream is dialled
// with. The owner's, always: RtspCredentials never leave the owning row.
public sealed class CameraShare
{
    public Guid CameraId { get; set; }
    public int UserId { get; set; }
    public CameraSharePermission Permission { get; set; } = CameraSharePermission.View;
    public DateTimeOffset CreatedAt { get; set; }
}
