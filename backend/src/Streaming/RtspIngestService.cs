using Microsoft.EntityFrameworkCore;
using Namorix.Scout.Models;
using Namorix.Scout.Persistence;
using Namorix.Scout.Services;

namespace Namorix.Scout.Streaming;

public sealed class RtspIngestService(
    IDbContextFactory<ScoutDbContext> dbFactory,
    ScoutSecretProtector secretProtector,
    CameraChangeSignal changes,
    ILoggerFactory loggerFactory,
    ILogger<RtspIngestService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    // The signature records the fields the ingest actually dials with, so a row whose
    // LastUpdatedAt moved for some other reason (a rename) is left alone.
    private sealed record IngestEntry(CameraRtspClient Client, string Signature, DateTimeOffset LastUpdatedAt);

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, IngestEntry> _clients = new();

    public CameraRtspClient? GetActiveClient(Guid cameraId)
    {
        lock (_gate)
        {
            return _clients.TryGetValue(cameraId, out var entry) ? entry.Client : null;
        }
    }

    // No entry means the camera is disabled, removed, or not picked up yet - all of which
    // read the same from the outside: nothing is being pulled for it.
    public CameraRuntimeStatus GetStatus(Guid cameraId)
    {
        lock (_gate)
        {
            return _clients.TryGetValue(cameraId, out var entry)
                ? entry.Client.GetStatus()
                : new CameraRuntimeStatus(CameraRuntimeState.Stopped, null, null);
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
                        var signature = Signature(camera);
                        if (_clients.TryGetValue(camera.Id, out var existing))
                        {
                            // Nothing wrote to the row since the last pass, so the running
                            // client is still faithful to it.
                            if (existing.LastUpdatedAt == camera.LastUpdatedAt)
                                continue;

                            if (existing.Signature == signature)
                            {
                                // Only fields the ingest ignores changed (retention, record
                                // flag); reloading here would drop the stream for nothing.
                                _clients[camera.Id] = existing with { LastUpdatedAt = camera.LastUpdatedAt };
                                continue;
                            }

                            // The URL or credentials moved under a running client, which would
                            // otherwise keep pulling from the old address until a restart. The
                            // client is reconfigured rather than replaced because viewer sessions
                            // hold it and would never see frames from a new object.
                            existing.Client.UpdateConfig(
                                camera.Name, camera.RtspUrl, secretProtector.Unprotect(camera.RtspCredentials));
                            _clients[camera.Id] = existing with
                            {
                                Signature = signature,
                                LastUpdatedAt = camera.LastUpdatedAt,
                            };
                            logger.LogInformation(
                                "Reloading RTSP ingest for camera {cameraId} ({name}) after a config change.",
                                camera.Id, camera.Name);
                            continue;
                        }

                        var credentials = secretProtector.Unprotect(camera.RtspCredentials);
                        var client = new CameraRtspClient(
                            camera.Id, camera.Name, camera.RtspUrl, credentials, loggerFactory);
                        _clients[camera.Id] = new IngestEntry(client, signature, camera.LastUpdatedAt);
                        client.Start();
                        logger.LogInformation("Started RTSP ingest for camera {cameraId} ({name}).",
                            camera.Id, camera.Name);
                    }

                    foreach (var stale in _clients.Keys.Except(enabled.Select(c => c.Id)).ToArray())
                    {
                        toStop ??= [];
                        toStop.Add(_clients[stale].Client);
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
                await changes.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // The name is excluded on purpose: the client only ever puts it in log messages, so a
    // rename does not justify tearing the stream down. The separator is a unit separator
    // because both remaining fields are free text that may contain any printable character.
    private static string Signature(ScCamera camera) =>
        $"{camera.RtspUrl}\u001f{camera.RtspCredentials}";

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        List<CameraRtspClient> clients;
        lock (_gate)
        {
            clients = _clients.Values.Select(e => e.Client).ToList();
            _clients.Clear();
        }

        foreach (var client in clients)
            await client.StopAsync();

        await base.StopAsync(cancellationToken);
    }
}
