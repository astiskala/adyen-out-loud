using System.Globalization;
using System.Resources;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

/// <summary>
/// Localization service using .NET RESX resources.
/// </summary>
public sealed class ResxLocalizationService : ILocalizationService
{
    private static readonly ResourceManager Resources = new("AdyenOutLoud.Resources.Strings", typeof(ResxLocalizationService).Assembly);

    /// <inheritdoc />
    public string CreatePaymentAnnouncement(PaymentMessage message, AppLanguage language)
    {
        var culture = CultureInfo.GetCultureInfo(language.Locale);
        return Get("PaymentReceived", culture);
    }

    /// <inheritdoc />
    public string CreateTestAnnouncement(AppLanguage language)
    {
        var culture = CultureInfo.GetCultureInfo(language.Locale);
        return Get("TestAnnouncement", culture);
    }

    private static string Get(string key, CultureInfo culture) =>
        Resources.GetString(key, culture) ?? throw new MissingManifestResourceException($"Missing resource '{key}' for '{culture.Name}'.");
}
