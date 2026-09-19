using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

/// <summary>Service managing the relay WebSocket connection and payment announcements.</summary>
public interface IRelayConnectionService
{
    /// <summary>Raised when the connection status changes.</summary>
    event EventHandler<RelayStatus>? StatusChanged;
    /// <summary>Raised with diagnostic messages for the user.</summary>
    event EventHandler<string>? Diagnostic;
    /// <summary>Raised when a payment announcement completes, played or not.</summary>
    event EventHandler<AnnouncementResult>? AnnouncementCompleted;

    /// <summary>Announces a payment unless it was already announced.</summary>
    /// <param name="message">The payment to announce.</param>
    /// <param name="ct">Cancels the announcement.</param>
    /// <returns>What happened.</returns>
    Task<AnnouncementResult> AnnounceAsync(PaymentMessage message, CancellationToken ct);
    /// <summary>Starts listening, unless already running.</summary>
    void Start();
    /// <summary>Stops listening.</summary>
    /// <returns>A task that completes once stopped.</returns>
    Task StopAsync();
}
