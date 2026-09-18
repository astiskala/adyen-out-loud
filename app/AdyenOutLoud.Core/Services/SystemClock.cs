using AdyenOutLoud.Abstractions;

namespace AdyenOutLoud.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
