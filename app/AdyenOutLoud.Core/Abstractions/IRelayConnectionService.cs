using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

/// <summary>
/// Service for managing the persistent WebSocket connection to the relay and payment announcements.
/// </summary>
public interface IRelayConnectionService
{
    /// <summary>
    /// Event raised when the connection status changes.
    /// </summary>
    event EventHandler<RelayStatus>? StatusChanged;

    /// <summary>
    /// Event raised for diagnostic/logging messages from the connection.
    /// </summary>
    event EventHandler<string>? Diagnostic;

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

    /// <summary>
    /// Starts the relay connection (or restarts if already running).
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the relay connection gracefully.
    /// </summary>
    /// <returns>A task representing the asynchronous stop operation.</returns>
    Task StopAsync();
}
