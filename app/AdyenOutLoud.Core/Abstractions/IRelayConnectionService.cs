using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

public interface IRelayConnectionService
{
    event EventHandler<RelayStatus>? StatusChanged;
    event EventHandler<string>? Diagnostic;
    void Start();
    Task StopAsync();
}
