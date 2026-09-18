using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

public sealed class PaymentAnnouncementService(
    ISettingsService settings,
    ITextToSpeechService textToSpeech,
    ILocalizationService localization,
    IClock clock) : IPaymentAnnouncementService
{
    public event EventHandler<AnnouncementResult>? AnnouncementCompleted;

    public async Task<AnnouncementResult> AnnounceAsync(PaymentMessage message, CancellationToken cancellationToken)
    {
        var isNew = await settings.TryReserveEventIdAsync(message.Id, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (!isNew)
        {
            return Complete(new(message.Id, true, true, false, "Duplicate acknowledged without speech.", message, null));
        }

        if (message.Amount is null)
        {
            return Complete(new(message.Id, true, false, false, "Payment has no amount; acknowledged without speech.", message, null));
        }

        try
        {
            var language = settings.SelectedLanguage;
            var text = localization.CreatePaymentAnnouncement(message, language);
            var diagnostic = await textToSpeech.SpeakAsync(text, language, cancellationToken).ConfigureAwait(false);
            return Complete(new(message.Id, true, false, true, diagnostic.Message, message, diagnostic));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Complete(new(message.Id, true, false, false, $"Payment recorded; voice failed: {exception.Message}", message, null));
        }
    }

    private AnnouncementResult Complete(AnnouncementResult result)
    {
        try
        {
            AnnouncementCompleted?.Invoke(this, result);
        }
        catch (Exception)
        {
            // UI observers must never prevent the relay acknowledgment.
        }
        return result;
    }
}
