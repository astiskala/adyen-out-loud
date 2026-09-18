using AdyenOutLoud.Abstractions;

namespace AdyenOutLoud.Services;

/// <summary>
/// iOS is deliberately foreground-only: aggressively suspending backgrounded apps means the only
/// realistic way to keep a WebSocket open is registering the "audio" background mode, which Apple
/// can reject for an app that isn't actually playing continuous audio. This reproduces exactly
/// today's pre-plan behavior on this one platform — stop the relay connection on background, and
/// let the next EnterForeground()-triggered relay.Start() (in AppLifecycleCoordinator) reconnect it.
/// </summary>
public sealed class BackgroundExecutionService(IRelayConnectionService relayConnection) : IBackgroundExecutionService
{
    public Task EnterForegroundAsync() => Task.CompletedTask;
    public Task EnterBackgroundAsync() => relayConnection.StopAsync();
}
