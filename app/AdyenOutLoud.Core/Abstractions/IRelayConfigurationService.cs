using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

public interface IRelayConfigurationService
{
    Task<RelayConfiguration?> GetAsync(CancellationToken cancellationToken = default);
    Task<RelayConfiguration> SaveAsync(Uri baseUrl, string terminalSerial, CancellationToken cancellationToken = default);
}
