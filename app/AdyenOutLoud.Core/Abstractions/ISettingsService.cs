using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

public interface ISettingsService
{
    AppLanguage SelectedLanguage { get; set; }
    Task<bool> TryReserveEventIdAsync(string eventId, DateTimeOffset receivedAt, CancellationToken cancellationToken);
}
