namespace AdyenOutLoud.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
