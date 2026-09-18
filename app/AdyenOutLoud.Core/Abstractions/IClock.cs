namespace AdyenOutLoud.Abstractions;

/// <summary>
/// Provides access to the current UTC time, allowing for testability.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Gets the current UTC date and time.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}
