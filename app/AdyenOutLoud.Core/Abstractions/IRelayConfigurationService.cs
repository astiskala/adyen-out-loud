using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

/// <summary>Pairs this device with a terminal and provides the resulting relay configuration.</summary>
public interface IRelayConfigurationService
{
    /// <summary>The saved configuration, or null if this device has not been paired.</summary>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The configuration, if any.</returns>
    Task<RelayConfiguration?> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Pairs this device with a terminal by quoting the last characters of the PSP reference on recent receipts,
    /// then saves the configuration.
    /// </summary>
    /// <param name="terminalSerial">The terminal serial number.</param>
    /// <param name="receiptCodes">The last 4 characters of the PSP reference on each of two recent receipts.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The new configuration.</returns>
    /// <exception cref="RelayPairingException">The input is invalid or the relay refused to pair.</exception>
    Task<RelayConfiguration> PairAsync(string terminalSerial, IReadOnlyList<string> receiptCodes, CancellationToken ct = default);
}
