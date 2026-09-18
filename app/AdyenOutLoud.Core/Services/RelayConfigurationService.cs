using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

/// <summary>
/// Implements <see cref="IRelayConfigurationService"/> using a persistent store.
/// </summary>
public sealed class RelayConfigurationService(IRelayConfigurationStore store) : IRelayConfigurationService
{
    /// <inheritdoc />
    public async Task<RelayConfiguration?> GetAsync(CancellationToken cancellationToken = default)
    {
        var saved = await store.GetAsync().ConfigureAwait(false);
        return saved is { } value ? Build(value.BaseUrl, value.TerminalSerial) : null;
    }

    /// <inheritdoc />
    public async Task<RelayConfiguration> SaveAsync(Uri baseUrl, string terminalSerial, CancellationToken cancellationToken = default)
    {
        var configuration = Build(baseUrl, terminalSerial);
        await store.SetAsync(baseUrl, terminalSerial).ConfigureAwait(false);
        return configuration;
    }

    private static RelayConfiguration Build(Uri baseUrl, string terminalSerial) =>
        new(baseUrl, terminalSerial, RelayEndpointFactory.CreateWebSocketUrl(baseUrl, terminalSerial));
}
