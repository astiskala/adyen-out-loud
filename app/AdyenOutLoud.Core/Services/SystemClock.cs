using AdyenOutLoud.Abstractions;

namespace AdyenOutLoud.Services;

/// <summary>
/// System clock implementation using <see cref="DateTimeOffset.UtcNow"/>.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
