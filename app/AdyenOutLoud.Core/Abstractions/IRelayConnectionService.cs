using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

/// <summary>
/// Service for managing the persistent WebSocket connection to the relay.
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
    /// Starts the relay connection (or restarts if already running).
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the relay connection gracefully.
    /// </summary>
    /// <returns>A task representing the asynchronous stop operation.</returns>
    Task StopAsync();
}
