using AdyenOutLoud.Abstractions;

namespace AdyenOutLoud.Services;

public sealed class TaskRetryDelay : IRetryDelay
{
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
