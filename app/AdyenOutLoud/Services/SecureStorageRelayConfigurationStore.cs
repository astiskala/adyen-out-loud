using AdyenOutLoud.Abstractions;
using Microsoft.Maui.Storage;

namespace AdyenOutLoud.Services;

/// <summary>
/// SecureStorage implementation of <see cref="IRelayConfigurationStore"/>.
/// </summary>
public sealed class SecureStorageRelayConfigurationStore : IRelayConfigurationStore
{
    private const string TerminalSerialKey = "relay-terminal-serial-v1";

    /// <inheritdoc />
    public async Task<string?> GetAsync()
    {
        var terminalSerial = await SecureStorage.Default.GetAsync(TerminalSerialKey).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(terminalSerial) ? null : terminalSerial;
    }

    /// <inheritdoc />
    public Task SetAsync(string terminalSerial) => SecureStorage.Default.SetAsync(TerminalSerialKey, terminalSerial);
}
