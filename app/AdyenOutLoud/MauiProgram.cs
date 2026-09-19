using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Services;
using AdyenOutLoud.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;

namespace AdyenOutLoud;

/// <summary>
/// MAUI application builder.
/// </summary>
public static class MauiProgram
{
    /// <summary>
    /// Creates the MAUI application.
    /// </summary>
    /// <returns>The configured <see cref="MauiApp"/>.</returns>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<ITextToSpeechService, MauiSpeechService>();
        builder.Services.AddSingleton<ILocalizationService, ResxLocalizationService>();
        builder.Services.AddSingleton<ISettingsService, PreferencesSettingsService>();
        builder.Services.AddSingleton<IRelayConfigurationStore, SecureStorageRelayConfigurationStore>();
        builder.Services.AddSingleton<IRelayConfigurationService, RelayConfigurationService>();
        builder.Services.AddSingleton<IRelayConnectionFactory, ClientWebSocketConnectionFactory>();
        builder.Services.AddSingleton<IRelayConnectionService>(provider => new RelayConnectionService(
            provider.GetRequiredService<IRelayConfigurationService>(),
            provider.GetRequiredService<IRelayConnectionFactory>(),
            provider.GetRequiredService<ISettingsService>(),
            provider.GetRequiredService<ITextToSpeechService>(),
            provider.GetRequiredService<ILocalizationService>()));
        builder.Services.AddSingleton<IBackgroundExecutionService, BackgroundExecutionService>();
        builder.Services.AddSingleton<AppLifecycleCoordinator>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}
