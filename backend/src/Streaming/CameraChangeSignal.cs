namespace Namorix.Scout.Streaming;

// Lets a camera write wake the ingest loop instead of waiting out its poll interval. The
// poll stays as the safety net for rows changed outside the API (direct edits to scout.db).
public sealed class CameraChangeSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A reconcile is already pending, which covers this change too.
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) =>
        _signal.WaitAsync(timeout, ct);
}
