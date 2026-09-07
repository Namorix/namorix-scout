using Microsoft.EntityFrameworkCore;
using Namorix.Scout.Persistence;
using Namorix.Scout.Services;

namespace Namorix.Scout.Streaming;

public sealed class RtspIngestService(
    IDbContextFactory<ScoutDbContext> dbFactory,
    ScoutSecretProtector secretProtector,
    ILoggerFactory loggerFactory,
    ILogger<RtspIngestService> logger) : BackgroundService
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, CameraRtspClient> _clients = new();

    public CameraRtspClient? GetActiveClient(Guid cameraId)
    {
        lock (_gate)
        {
            return _clients.TryGetValue(cameraId, out var client) ? client : null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RTSP ingest: reading enabled cameras from the database.");

        while (!stoppingToken.IsCancellationRequested)
        {
            List<CameraRtspClient>? toStop = null;
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                var enabled = await db.Cameras.AsNoTracking()
                    .Where(c => c.Enabled)
                    .OrderBy(c => c.Id)
                    .ToListAsync(stoppingToken);

                lock (_gate)
                {
                    foreach (var camera in enabled)
                    {
                        if (_clients.ContainsKey(camera.Id))
                            continue;

                        var credentials = secretProtector.Unprotect(camera.RtspCredentials);
                        var client = new CameraRtspClient(
                            camera.Id, camera.Name, camera.RtspUrl, credentials, loggerFactory);
                        _clients[camera.Id] = client;
                        client.Start();
                        logger.LogInformation("Started RTSP ingest for camera {cameraId} ({name}).",
                            camera.Id, camera.Name);
                    }

                    foreach (var stale in _clients.Keys.Except(enabled.Select(c => c.Id)).ToArray())
                    {
                        toStop ??= [];
                        toStop.Add(_clients[stale]);
                        _clients.Remove(stale);
                        logger.LogInformation("Stopped RTSP ingest for camera {cameraId} (disabled or removed).", stale);
                    }
                }

                if (toStop is not null)
                {
                    foreach (var client in toStop)
                        await client.StopAsync();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reconcile RTSP ingest cameras.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        List<CameraRtspClient> clients;
        lock (_gate)
        {
            clients = _clients.Values.ToList();
            _clients.Clear();
        }

        foreach (var client in clients)
            await client.StopAsync();

        await base.StopAsync(cancellationToken);
    }
}
