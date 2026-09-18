using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

/// <summary>
/// Service for managing application settings.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Gets or sets the currently selected language for announcements.
    /// </summary>
    AppLanguage SelectedLanguage { get; set; }

    /// <summary>
    /// Attempts to reserve an event ID to prevent duplicate announcements.
    /// </summary>
    /// <param name="eventId">The unique event identifier.</param>
    /// <param name="receivedAt">The timestamp when the event was received.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if the event ID was reserved (first time seeing this event); false if duplicate.</returns>
    Task<bool> TryReserveEventIdAsync(string eventId, DateTimeOffset receivedAt, CancellationToken cancellationToken);
}
