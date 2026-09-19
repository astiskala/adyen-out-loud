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
    /// Saves the terminal serial number and returns the resulting relay configuration.
    /// </summary>
    /// <param name="terminalSerial">The terminal serial number this app is configured for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The saved relay configuration with computed WebSocket URL.</returns>
    Task<RelayConfiguration> SaveAsync(string terminalSerial, CancellationToken cancellationToken = default);
}
