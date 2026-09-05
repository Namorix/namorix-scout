using Microsoft.AspNetCore.SignalR;
using Namorix.Core.Hubs;
using Namorix.Scout.Constants;

namespace Namorix.Scout.Hubs;

public sealed class ScoutHub(ILogger<NmxHub> logger) : NmxHub(logger)
{
    public async Task Subscribe()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, ScoutSignalRGroups.Scout);
    }

    public async Task Unsubscribe()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ScoutSignalRGroups.Scout);
    }
}
