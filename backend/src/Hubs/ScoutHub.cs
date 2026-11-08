using Microsoft.AspNetCore.SignalR;
using Namorix.Core.Hubs;
using Namorix.Scout.Constants;

namespace Namorix.Scout.Hubs;

public sealed class ScoutHub(ILogger<NmxHub> logger) : NmxHub(logger)
{
    // Names follow the @namorix/core group convention (Subscribe + PascalCase group),
    // so useSignalRGroup("scout", true) resolves them.
    public async Task SubscribeScout()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, ScoutSignalRGroups.Scout);
    }

    public async Task UnsubscribeScout()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ScoutSignalRGroups.Scout);
    }
}
