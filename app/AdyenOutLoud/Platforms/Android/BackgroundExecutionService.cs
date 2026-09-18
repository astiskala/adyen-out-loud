using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Platforms.Android;
using Android.Content;

namespace AdyenOutLoud.Services;

public sealed class BackgroundExecutionService : IBackgroundExecutionService
{
    public Task EnterForegroundAsync()
    {
        var context = global::Android.App.Application.Context;
        context.StopService(new Intent(context, typeof(PaymentListenerForegroundService)));
        return Task.CompletedTask;
    }

    public Task EnterBackgroundAsync()
    {
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
    }
}
