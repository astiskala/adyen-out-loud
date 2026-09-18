using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

public interface IPaymentAnnouncementService
{
    event EventHandler<AnnouncementResult>? AnnouncementCompleted;
    Task<AnnouncementResult> AnnounceAsync(PaymentMessage message, CancellationToken cancellationToken);
}
