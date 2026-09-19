using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

/// <summary>
/// Service for managing relay configuration (retrieve and save the terminal serial).
/// </summary>
/// <param name="store">Where the terminal serial is persisted.</param>
/// <param name="relayUrl">The base HTTPS URL of the relay; the same for every installation.</param>
public sealed class RelayConfigurationService(IRelayConfigurationStore store, Uri relayUrl) : IRelayConfigurationService
{
    /// <inheritdoc />
    public async Task<RelayConfiguration?> GetAsync(CancellationToken cancellationToken = default)
    {
        var serial = await store.GetAsync().ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(serial) ? null : Build(serial);
    }

    /// <inheritdoc />
    public async Task<RelayConfiguration> SaveAsync(string terminalSerial, CancellationToken cancellationToken = default)
    {
        var configuration = Build(terminalSerial);
        await store.SetAsync(terminalSerial).ConfigureAwait(false);
        return configuration;
    }

    private RelayConfiguration Build(string terminalSerial) =>
        new(terminalSerial, RelayEndpointFactory.CreateWebSocketUrl(relayUrl, terminalSerial));
}
