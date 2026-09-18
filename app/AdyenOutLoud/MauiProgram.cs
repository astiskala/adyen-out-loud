using System.Reflection;
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

        var relayUri = GetRelayUri();
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IRetryDelay, TaskRetryDelay>();
        builder.Services.AddSingleton<ITextToSpeechService, MauiSpeechService>();
        builder.Services.AddSingleton<ILocalizationService, ResxLocalizationService>();
        builder.Services.AddSingleton<ISettingsService, PreferencesSettingsService>();
        builder.Services.AddSingleton<IInstanceTokenStore, SecureStorageTokenStore>();
        builder.Services.AddSingleton<IInstanceIdentityService>(provider =>
            new InstanceIdentityService(relayUri, provider.GetRequiredService<IInstanceTokenStore>()));
        builder.Services.AddSingleton<IRelayConnectionFactory, ClientWebSocketConnectionFactory>();
        builder.Services.AddSingleton<IPaymentAnnouncementService, PaymentAnnouncementService>();
        builder.Services.AddSingleton<IRelayConnectionService>(provider => new RelayConnectionService(
            provider.GetRequiredService<IInstanceIdentityService>(),
            provider.GetRequiredService<IRelayConnectionFactory>(),
            provider.GetRequiredService<IPaymentAnnouncementService>(),
            provider.GetRequiredService<IRetryDelay>()));
        builder.Services.AddSingleton<AppLifecycleCoordinator>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }

    private static Uri GetRelayUri()
    {
        var value = typeof(MauiProgram).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "RelayBaseUrl").Value;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("RelayBaseUrl must be an HTTPS origin without a path, query, or fragment.");
        }

        return uri;
    }
}
