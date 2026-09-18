using AdyenOutLoud.Services;

namespace AdyenOutLoud.Tests;

public sealed class InfrastructureTests
{
    [Fact]
    public async Task TaskRetryDelayWaitsApproximatelyTheRequestedDuration()
    {
        var delay = new TaskRetryDelay();
        var before = DateTimeOffset.UtcNow;
        await delay.WaitAsync(TimeSpan.FromMilliseconds(20), CancellationToken.None);
        Assert.True(DateTimeOffset.UtcNow - before >= TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public async Task TaskRetryDelayHonorsCancellation()
    {
        var delay = new TaskRetryDelay();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(() => delay.WaitAsync(TimeSpan.FromSeconds(30), cts.Token));
    }

    [Fact]
    public void SystemClockReportsTheCurrentTime()
    {
        var clock = new SystemClock();
        Assert.True(DateTimeOffset.UtcNow - clock.UtcNow < TimeSpan.FromSeconds(5));
    }
}
