using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Services;
using AdyenOutLoud.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;

namespace AdyenOutLoud;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IRetryDelay, TaskRetryDelay>();
        builder.Services.AddSingleton<ITextToSpeechService, MauiSpeechService>();
        builder.Services.AddSingleton<ILocalizationService, ResxLocalizationService>();
        builder.Services.AddSingleton<ISettingsService, PreferencesSettingsService>();
        builder.Services.AddSingleton<IRelayConfigurationStore, SecureStorageRelayConfigurationStore>();
        builder.Services.AddSingleton<IRelayConfigurationService, RelayConfigurationService>();
        builder.Services.AddSingleton<IRelayConnectionFactory, ClientWebSocketConnectionFactory>();
        builder.Services.AddSingleton<IPaymentAnnouncementService, PaymentAnnouncementService>();
        builder.Services.AddSingleton<IRelayConnectionService>(provider => new RelayConnectionService(
            provider.GetRequiredService<IRelayConfigurationService>(),
            provider.GetRequiredService<IRelayConnectionFactory>(),
            provider.GetRequiredService<IPaymentAnnouncementService>(),
            provider.GetRequiredService<IRetryDelay>()));
        builder.Services.AddSingleton<IBackgroundExecutionService, BackgroundExecutionService>();
        builder.Services.AddSingleton<AppLifecycleCoordinator>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}
