namespace Namorix.Scout.Streaming;

// Muxing a camera costs CPU for as long as it runs, so a packager lives only while somebody is
// watching - but not a moment less: a viewer reloads the playlist every couple of seconds, and
// tearing the packager down between reloads would put every poll back behind the wait for the
// camera's next keyframe.
public sealed class HlsPackagerRegistry : IDisposable
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(15);

    private sealed class Entry(HlsPackager packager, CameraRtspClient client)
    {
        public HlsPackager Packager { get; } = packager;
        public CameraRtspClient Client { get; } = client;
        public DateTimeOffset LastUsed { get; set; } = DateTimeOffset.UtcNow;
    }

    private readonly RtspIngestService _ingest;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<HlsPackagerRegistry> _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly Timer _sweep;

    private bool _disposed;

    public HlsPackagerRegistry(
        RtspIngestService ingest,
        ILoggerFactory loggerFactory,
        ILogger<HlsPackagerRegistry> logger)
    {
        _ingest = ingest;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _sweep = new Timer(_ => Sweep(), null, SweepInterval, SweepInterval);
    }

    // Null means the camera has no ingest running - disabled, removed, or not dialed yet - which
    // the caller reports the same way the WebRTC path reports a camera it cannot reach.
    public HlsPackager? Acquire(Guid cameraId)
    {
        lock (_gate)
        {
            if (_disposed) return null;

            var client = _ingest.GetActiveClient(cameraId);

            if (_entries.TryGetValue(cameraId, out var existing))
            {
                // A different client object means the camera was reconfigured or the connection
                // was rebuilt. The packager's timeline belongs to the stream that ended, so
                // hanging on to it would append new frames to a fragment already handed out.
                if (client is not null && ReferenceEquals(client, existing.Client))
                {
                    existing.LastUsed = DateTimeOffset.UtcNow;
                    return existing.Packager;
                }

                Remove(cameraId, existing);
            }

            if (client is null) return null;

            var packager = new HlsPackager(_loggerFactory.CreateLogger<HlsPackager>());
            client.FrameReceived += packager.PushFrame;
            _entries[cameraId] = new Entry(packager, client);
            _logger.LogInformation("HLS packager started for camera {cameraId}.", cameraId);
            return packager;
        }
    }

    private void Remove(Guid cameraId, Entry entry)
    {
        entry.Client.FrameReceived -= entry.Packager.PushFrame;
        entry.Packager.Dispose();
        _entries.Remove(cameraId);
    }

    private void Sweep()
    {
        List<(Guid CameraId, Entry Entry)> idle;
        lock (_gate)
        {
            if (_disposed) return;
            var cutoff = DateTimeOffset.UtcNow - IdleTimeout;
            idle = _entries
                .Where(pair => pair.Value.LastUsed < cutoff)
                .Select(pair => (pair.Key, pair.Value))
                .ToList();

            foreach (var (cameraId, entry) in idle)
                Remove(cameraId, entry);
        }

        foreach (var (cameraId, _) in idle)
            _logger.LogInformation("HLS packager stopped for camera {cameraId} after {seconds}s idle.",
                cameraId, (int)IdleTimeout.TotalSeconds);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var (cameraId, entry) in _entries.ToList())
                Remove(cameraId, entry);
        }

        _sweep.Dispose();
    }
}
