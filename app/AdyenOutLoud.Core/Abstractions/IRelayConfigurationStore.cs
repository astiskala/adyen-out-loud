namespace AdyenOutLoud.Abstractions;

/// <summary>
/// Persistent storage for the terminal serial number this device listens for.
/// </summary>
public interface IRelayConfigurationStore
{
    /// <summary>
    /// Gets the stored terminal serial number.
    /// </summary>
    /// <returns>The stored serial, or null if not set.</returns>
    Task<string?> GetAsync();

    /// <summary>
    /// Stores the terminal serial number.
    /// </summary>
    /// <param name="terminalSerial">The terminal serial number this app is configured for.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetAsync(string terminalSerial);
}
