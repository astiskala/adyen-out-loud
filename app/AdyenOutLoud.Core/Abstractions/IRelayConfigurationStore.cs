namespace AdyenOutLoud.Abstractions;

/// <summary>
/// Persistent storage for relay configuration (base URL and terminal serial).
/// </summary>
public interface IRelayConfigurationStore
{
    /// <summary>
    /// Gets the stored relay configuration.
    /// </summary>
    /// <returns>The stored base URL and terminal serial, or null if not set.</returns>
    Task<(Uri BaseUrl, string TerminalSerial)?> GetAsync();

    /// <summary>
    /// Stores the relay configuration.
    /// </summary>
    /// <param name="baseUrl">The base HTTPS URL of the relay service.</param>
    /// <param name="terminalSerial">The terminal serial number this app is configured for.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetAsync(Uri baseUrl, string terminalSerial);
}

