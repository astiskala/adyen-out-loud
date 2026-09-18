using AdyenOutLoud.Abstractions;

namespace AdyenOutLoud.Services;

/// <summary>Mac Catalyst apps run as ordinary macOS processes and are not suspended when the
/// window loses focus or is minimized, so there is nothing to do here — the relay connection is
/// simply never stopped on background/foreground transitions on this platform.</summary>
public sealed class BackgroundExecutionService : IBackgroundExecutionService
{
    public Task EnterForegroundAsync() => Task.CompletedTask;
    public Task EnterBackgroundAsync() => Task.CompletedTask;
}
