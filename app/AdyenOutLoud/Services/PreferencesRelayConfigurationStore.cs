using AdyenOutLoud.Abstractions;
using Microsoft.Maui.Storage;

namespace AdyenOutLoud.Services;

/// <summary>
/// <see cref="Preferences"/> implementation of <see cref="IRelayConfigurationStore"/>. The terminal serial is not a
/// secret, and Keychain-backed <c>SecureStorage</c> needs entitlements unsigned/ad-hoc-signed Mac builds don't have.
/// </summary>
public sealed class PreferencesRelayConfigurationStore : IRelayConfigurationStore
{
    private const string TerminalSerialKey = "relay-terminal-serial-v1";

    /// <inheritdoc />
    public Task<string?> GetAsync()
    {
        var terminalSerial = Preferences.Default.Get<string?>(TerminalSerialKey, null);
        return Task.FromResult(string.IsNullOrWhiteSpace(terminalSerial) ? null : terminalSerial);
    }

    /// <inheritdoc />
    public Task SetAsync(string terminalSerial)
    {
        Preferences.Default.Set(TerminalSerialKey, terminalSerial);
        return Task.CompletedTask;
    }
}
