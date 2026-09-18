using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

/// <summary>
/// Service for creating localized announcement strings.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Creates a localized payment announcement message.
    /// </summary>
    /// <param name="message">The payment message containing transaction details.</param>
    /// <param name="language">The target language for localization.</param>
    /// <returns>A localized string announcing the successful payment.</returns>
    string CreatePaymentAnnouncement(PaymentMessage message, AppLanguage language);

    /// <summary>
    /// Creates a localized test announcement message.
    /// </summary>
    /// <param name="language">The target language for localization.</param>
    /// <returns>A localized string for test announcements.</returns>
    string CreateTestAnnouncement(AppLanguage language);
}
