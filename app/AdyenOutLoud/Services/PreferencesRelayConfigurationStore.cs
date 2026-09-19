using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;
using Microsoft.Maui.Storage;

namespace AdyenOutLoud.Services;

/// <summary>
/// <see cref="Preferences"/> implementation of <see cref="IRelayConfigurationStore"/>. The device token lives in the
/// app's private preferences (excluded from Android backups) rather than Keychain-backed <c>SecureStorage</c>, which
/// needs entitlements that unsigned/ad-hoc-signed Mac builds don't have. It only lets a device listen to one terminal.
/// </summary>
public sealed class PreferencesRelayConfigurationStore : IRelayConfigurationStore
{
    private const string TerminalSerialKey = "relay-terminal-serial-v1";
    private const string AccessTokenKey = "relay-access-token-v1";

    /// <inheritdoc />
    public Task<RelayPairing?> GetAsync()
    {
        var serial = Preferences.Default.Get<string?>(TerminalSerialKey, null);
        var token = Preferences.Default.Get<string?>(AccessTokenKey, null);
        return Task.FromResult(string.IsNullOrWhiteSpace(serial) || string.IsNullOrWhiteSpace(token) ? null : new RelayPairing(serial, token));
    }

    /// <inheritdoc />
    public Task SetAsync(RelayPairing pairing)
    {
        ArgumentNullException.ThrowIfNull(pairing);
        Preferences.Default.Set(TerminalSerialKey, pairing.TerminalSerial);
        Preferences.Default.Set(AccessTokenKey, pairing.AccessToken);
        return Task.CompletedTask;
    }
}
