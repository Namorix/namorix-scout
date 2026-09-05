using Namorix.Core.Grpc;
using Namorix.Core.Protos;

namespace Namorix.Scout.Services;

public class ScoutService(AddonChannelClient channel, ILogger<ScoutService> logger)
    : AddonHostedServiceBase(channel, logger)
{
    protected override void ConfigureHandlers(AddonChannelClient channel)
    {
        channel.OnMessage += msg =>
        {
            logger.LogInformation("Received from Namorix: type={Type}, payload={Payload}",
                msg.Type, msg.Payload);
        };
    }

    protected override async Task OnConnectedAsync(CancellationToken ct)
    {
        await Channel.SendAsync(new AddonMessage
        {
            Type = "widget-event",
            Payload = "{\"event\":\"ready\"}",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }, ct);

        logger.LogInformation("gRPC channel connected");
    }
}
