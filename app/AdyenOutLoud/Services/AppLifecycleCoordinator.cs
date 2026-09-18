using AdyenOutLoud.Abstractions;

namespace AdyenOutLoud.Services;

public sealed class AppLifecycleCoordinator(IRelayConnectionService relayConnection, IBackgroundExecutionService background)
{
    private int _isForeground;

    public void EnterForeground()
    {
        if (Interlocked.Exchange(ref _isForeground, 1) == 0)
        {
            _ = background.EnterForegroundAsync();
            relayConnection.Start();
        }
    }

    public async Task LeaveForegroundAsync()
    {
        if (Interlocked.Exchange(ref _isForeground, 0) == 1)
        {
            await background.EnterBackgroundAsync();
        }
    }
}
