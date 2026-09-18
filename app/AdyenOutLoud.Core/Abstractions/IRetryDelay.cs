namespace AdyenOutLoud.Abstractions;

public interface IRetryDelay
{
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}
