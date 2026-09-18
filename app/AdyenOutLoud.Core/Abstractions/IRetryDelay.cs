namespace AdyenOutLoud.Abstractions;

/// <summary>
/// Abstraction for delay operations, allowing for testable retry logic.
/// </summary>
public interface IRetryDelay
{
    /// <summary>
    /// Waits for the specified delay, respecting cancellation.
    /// </summary>
    /// <param name="delay">The duration to wait.</param>
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    /// <returns>A task that completes after the delay or when cancelled.</returns>
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}
