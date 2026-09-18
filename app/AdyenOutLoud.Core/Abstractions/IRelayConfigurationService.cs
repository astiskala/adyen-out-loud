using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

/// <summary>
/// Service for managing relay configuration (retrieve and save).
/// </summary>
public interface IRelayConfigurationService
{
    /// <summary>
    /// Gets the saved relay configuration, if any.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The relay configuration, or null if not configured.</returns>
    Task<RelayConfiguration?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a new relay configuration.
    /// </summary>
    /// <param name="baseUrl">The base HTTPS URL of the relay service.</param>
    /// <param name="terminalSerial">The terminal serial number this app is configured for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The saved relay configuration with computed WebSocket URL.</returns>
    Task<RelayConfiguration> SaveAsync(Uri baseUrl, string terminalSerial, CancellationToken cancellationToken = default);
}
