using System.Globalization;
using System.Resources;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

public sealed class ResxLocalizationService : ILocalizationService
{
    private static readonly ResourceManager Resources = new("AdyenOutLoud.Resources.Strings", typeof(ResxLocalizationService).Assembly);

    public string CreatePaymentAnnouncement(PaymentMessage message, AppLanguage language)
    {
        if (message.Amount is null)
        {
            throw new ArgumentException("A payment amount is required for an announcement.", nameof(message));
        }

        var culture = CultureInfo.GetCultureInfo(language.Locale);
        var template = Get("PaymentReceived", culture);
        var amount = CurrencyFormatter.Format(message.Amount.ValueMinor, message.Amount.Currency, language);
        var method = string.IsNullOrWhiteSpace(message.PaymentMethod)
            ? Get("UnknownPaymentMethod", culture)
            : PaymentMethodNames.Get(message.PaymentMethod);
        return string.Format(culture, template, amount, method);
    }

    public string CreateTestAnnouncement(AppLanguage language)
    {
        var culture = CultureInfo.GetCultureInfo(language.Locale);
        return Get("TestAnnouncement", culture);
    }

    private static string Get(string key, CultureInfo culture) =>
        Resources.GetString(key, culture) ?? throw new MissingManifestResourceException($"Missing resource '{key}' for '{culture.Name}'.");
}
