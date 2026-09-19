using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

/// <summary>Application settings including language and event deduplication.</summary>
public interface ISettingsService
{
    /// <summary>The announcement language.</summary>
    AppLanguage SelectedLanguage { get; set; }
    /// <summary>Records an event ID, unless it was already seen.</summary>
    /// <param name="eventId">The relay event ID.</param>
    /// <param name="receivedAt">When it was received.</param>
    /// <param name="ct">Cancels the operation.</param>
    /// <returns>True if the event is new and should be announced.</returns>
    Task<bool> TryReserveEventIdAsync(string eventId, DateTimeOffset receivedAt, CancellationToken ct);
}
