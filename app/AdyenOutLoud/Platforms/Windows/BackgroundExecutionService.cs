using AdyenOutLoud.Abstractions;

namespace AdyenOutLoud.Services;

/// <summary>Windows desktop apps are not suspended when minimized, so there is nothing to do here
/// — the relay connection is simply never stopped on background/foreground transitions on this
/// platform.</summary>
public sealed class BackgroundExecutionService : IBackgroundExecutionService
{
    public Task EnterForegroundAsync() => Task.CompletedTask;
    public Task EnterBackgroundAsync() => Task.CompletedTask;
}
