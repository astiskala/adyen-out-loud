using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

/// <summary>
/// Service for announcing successful payments via text-to-speech.
/// </summary>
public interface IPaymentAnnouncementService
{
    /// <summary>
    /// Event raised when a payment announcement completes (successfully or not).
    /// </summary>
    event EventHandler<AnnouncementResult>? AnnouncementCompleted;

    /// <summary>
    /// Announces a successful payment via text-to-speech.
    /// </summary>
    /// <param name="message">The payment message to announce.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result of the announcement attempt.</returns>
    Task<AnnouncementResult> AnnounceAsync(PaymentMessage message, CancellationToken cancellationToken);
}
