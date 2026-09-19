using AdyenOutLoud.Abstractions;
using Microsoft.Maui.Storage;

namespace AdyenOutLoud.Services;

/// <summary>
/// SecureStorage implementation of <see cref="IRelayConfigurationStore"/>.
/// </summary>
public sealed class SecureStorageRelayConfigurationStore : IRelayConfigurationStore
{
    private const string BaseUrlKey = "relay-base-url-v1";
    private const string TerminalSerialKey = "relay-terminal-serial-v1";

    /// <inheritdoc />
    public async Task<(Uri BaseUrl, string TerminalSerial)?> GetAsync()
    {
        var baseUrlText = await SecureStorage.Default.GetAsync(BaseUrlKey).ConfigureAwait(false);
        var terminalSerial = await SecureStorage.Default.GetAsync(TerminalSerialKey).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(baseUrlText) || string.IsNullOrWhiteSpace(terminalSerial)) return null;
        if (!Uri.TryCreate(baseUrlText, UriKind.Absolute, out var baseUrl)) return null;
        return (baseUrl, terminalSerial);
    }

    /// <inheritdoc />
    public async Task SetAsync(Uri baseUrl, string terminalSerial)
    {
        await SecureStorage.Default.SetAsync(BaseUrlKey, baseUrl.AbsoluteUri).ConfigureAwait(false);
        await SecureStorage.Default.SetAsync(TerminalSerialKey, terminalSerial).ConfigureAwait(false);
    }
}

