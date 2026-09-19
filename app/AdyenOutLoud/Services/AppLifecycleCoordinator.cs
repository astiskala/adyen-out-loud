using AdyenOutLoud.Abstractions;

namespace AdyenOutLoud.Services;

/// <summary>
/// Coordinates application lifecycle events with the relay connection.
/// </summary>
public sealed class AppLifecycleCoordinator(IRelayConnectionService relayConnection, IBackgroundExecutionService background)
{
    private int _isForeground;

    /// <summary>
    /// Called when the application enters the foreground.
    /// </summary>
    public void EnterForeground()
    {
        if (Interlocked.Exchange(ref _isForeground, 1) == 0)
        {
            _ = background.EnterForegroundAsync();
            relayConnection.Start();
        }
    }

    /// <summary>
    /// Called when the application leaves the foreground.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LeaveForegroundAsync()
    {
        if (Interlocked.Exchange(ref _isForeground, 0) == 1)
        {
            await background.EnterBackgroundAsync();
        }
    }
}
