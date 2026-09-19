using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

/// <summary>Persistent storage for the terminal serial and this device's token.</summary>
public interface IRelayConfigurationStore
{
    /// <summary>Reads the saved pairing, or null if this device has not been paired.</summary>
    /// <returns>The saved pairing, if any.</returns>
    Task<RelayPairing?> GetAsync();

    /// <summary>Saves a pairing, replacing any earlier one.</summary>
    /// <param name="pairing">The terminal serial and device token.</param>
    /// <returns>A task that completes once the pairing is saved.</returns>
    Task SetAsync(RelayPairing pairing);
}
