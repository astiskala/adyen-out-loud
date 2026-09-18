using AdyenOutLoud.Abstractions;

namespace AdyenOutLoud.Services;

/// <summary>
/// Retry delay implementation using <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
/// </summary>
public sealed class TaskRetryDelay : IRetryDelay
{
    /// <inheritdoc />
    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
