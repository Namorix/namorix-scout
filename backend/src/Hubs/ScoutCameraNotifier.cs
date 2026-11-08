using Microsoft.AspNetCore.SignalR;
using Namorix.Scout.Constants;

namespace Namorix.Scout.Hubs;

// The payload is only the camera id: each client re-reads its own list, because access
// (owner vs share) decides which cameras that list contains.
public sealed class ScoutCameraNotifier(IHubContext<ScoutHub> hub)
{
    public Task ChangedAsync(Guid cameraId, CancellationToken ct = default) =>
        hub.Clients.Group(ScoutSignalRGroups.Scout)
            .SendAsync(ScoutSignalREvents.CameraChanged, cameraId, ct);

    public Task DeletedAsync(Guid cameraId, CancellationToken ct = default) =>
        hub.Clients.Group(ScoutSignalRGroups.Scout)
            .SendAsync(ScoutSignalREvents.CameraDeleted, cameraId, ct);
}
