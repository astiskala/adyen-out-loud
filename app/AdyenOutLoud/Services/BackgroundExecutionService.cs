using AdyenOutLoud.Abstractions;

#if ANDROID
using AdyenOutLoud.Platforms.Android;
using Android.Content;
#endif

namespace AdyenOutLoud.Services;

/// <summary>
/// Platform-specific background execution management.
/// </summary>
/// <remarks>
/// <para><b>Android:</b> Starts a foreground service on background to keep the process alive,
/// stops it on foreground.</para>
/// <para><b>iOS:</b> Deliberately foreground-only — stops the relay connection on background,
/// reconnects on foreground. True background WebSocket would require "audio" background mode
/// which Apple may reject.</para>
/// <para><b>Mac Catalyst / Windows:</b> Desktop apps aren't suspended when minimized, so the
/// relay connection is never stopped on background/foreground transitions.</para>
/// </remarks>
public sealed class BackgroundExecutionService : IBackgroundExecutionService
{
#if IOS
    private readonly IRelayConnectionService _relayConnection;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundExecutionService"/> class for iOS.
    /// </summary>
    /// <param name="relayConnection">The relay connection service.</param>
    public BackgroundExecutionService(IRelayConnectionService relayConnection)
    {
        _relayConnection = relayConnection;
    }
#else
    /// <summary>
    /// Initializes a new instance of the <see cref="BackgroundExecutionService"/> class.
    /// </summary>
    public BackgroundExecutionService()
    {
    }
#endif

    /// <inheritdoc />
    public Task EnterForegroundAsync()
    {
#if ANDROID
        var context = global::Android.App.Application.Context;
        context.StopService(new Intent(context, typeof(PaymentListenerForegroundService)));
        return Task.CompletedTask;
#elif IOS
        return Task.CompletedTask;
#else
        return Task.CompletedTask;
#endif
    }

    /// <inheritdoc />
    public Task EnterBackgroundAsync()
    {
#if ANDROID
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(PaymentListenerForegroundService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
        return Task.CompletedTask;
#elif IOS
        return _relayConnection.StopAsync();
#else
        return Task.CompletedTask;
#endif
    }
}

