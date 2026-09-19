using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Services;
using AdyenOutLoud.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using Plugin.Maui.Audio;

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
        builder.UseMauiApp<App>().AddAudio();
        Handlers.InputChrome.Register();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<IAnnouncementPlayer, MauiAudioPlayer>();
        builder.Services.AddSingleton<ISettingsService, PreferencesSettingsService>();
        builder.Services.AddSingleton<IRelayConfigurationStore, PreferencesRelayConfigurationStore>();
        builder.Services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
        builder.Services.AddSingleton<IRelayConfigurationService>(provider => new RelayConfigurationService(
            provider.GetRequiredService<IRelayConfigurationStore>(),
            RelayUrl(),
            provider.GetRequiredService<HttpClient>()));
        builder.Services.AddSingleton<IRelayConnectionFactory, ClientWebSocketConnectionFactory>();
        builder.Services.AddSingleton<IRelayConnectionService>(provider => new RelayConnectionService(
            provider.GetRequiredService<IRelayConfigurationService>(),
            provider.GetRequiredService<IRelayConnectionFactory>(),
            provider.GetRequiredService<ISettingsService>(),
            provider.GetRequiredService<IAnnouncementPlayer>()));
        builder.Services.AddSingleton<IBackgroundExecutionService, BackgroundExecutionService>();
        builder.Services.AddSingleton<AppLifecycleCoordinator>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<Func<MainPage>>(provider => provider.GetRequiredService<MainPage>);

        return builder.Build();
    }

    // Every installation talks to the one hosted relay. Debug builds may point at a local test relay instead.
    private static Uri RelayUrl()
    {
#if DEBUG
        if (Uri.TryCreate(Environment.GetEnvironmentVariable("ADYEN_OUT_LOUD_RELAY_URL"), UriKind.Absolute, out var overrideUrl))
        {
            return overrideUrl;
        }
#endif
        return RelayEndpointFactory.DefaultBaseUrl;
    }
}
