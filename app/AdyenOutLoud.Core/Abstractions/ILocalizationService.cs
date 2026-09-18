using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

public interface ILocalizationService
{
    string CreatePaymentAnnouncement(PaymentMessage message, AppLanguage language);
    string CreateTestAnnouncement(AppLanguage language);
}
