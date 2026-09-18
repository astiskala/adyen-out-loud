using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace AdyenOutLoud.Platforms.Android;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class PaymentListenerForegroundService : Service
{
    private const string ChannelId = "payment-listener";
    private const int NotificationId = 1;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && manager.GetNotificationChannel(ChannelId) is null)
        {
            var channel = new NotificationChannel(ChannelId, "Payment listener", NotificationImportance.Low)
            {
                Description = "Keeps Adyen Out Loud listening for payments while the app is in the background.",
            };
            manager.CreateNotificationChannel(channel);
        }

        var builder = new NotificationCompat.Builder(this, ChannelId);
        builder.SetContentTitle("Adyen Out Loud");
        builder.SetContentText("Listening for payments in the background.");
        builder.SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo);
        builder.SetOngoing(true);
        var notification = builder.Build();
        if (notification is not null) StartForeground(NotificationId, notification);
        return StartCommandResult.Sticky;
    }
}
