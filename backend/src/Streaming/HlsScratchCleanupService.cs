using Namorix.Core.OAuth;
using Namorix.Scout.Constants;

namespace Namorix.Scout.Streaming;

// SharpMP4 removes its scratch file when the handle closes, which a killed process never reaches,
// so leftovers would otherwise sit in the working directory forever. Age is the only honest test
// left: a file untouched for a week cannot belong to a stream that is still running.
public sealed class HlsScratchCleanupService(
    NmxAddonConfig config,
    ILogger<HlsScratchCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("HLS scratch cleanup worker starting");
        Cleanup();

        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                Cleanup();
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("HLS scratch cleanup worker stopping");
        }
    }

    private void Cleanup()
    {
        try
        {
            var dir = Path.Combine(config.DataDir, ScoutDataPaths.HlsScratch);
            if (!Directory.Exists(dir)) return;

            var cutoff = DateTime.UtcNow - Retention;
            var deleted = 0;
            foreach (var file in Directory.GetFiles(dir))
            {
                if (File.GetLastWriteTimeUtc(file) >= cutoff) continue;
                File.Delete(file);
                deleted += 1;
            }

            if (deleted > 0)
                logger.LogInformation("Removed {count} stale HLS scratch files.", deleted);
        }
        catch (Exception ex)
        {
            // Another instance sharing this data directory may still hold the file open; a failed
            // sweep is not worth taking the host down for, the next one retries.
            logger.LogWarning(ex, "Failed to clean HLS scratch files");
        }
    }
}
