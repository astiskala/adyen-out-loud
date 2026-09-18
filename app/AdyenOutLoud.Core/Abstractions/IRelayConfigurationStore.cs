namespace AdyenOutLoud.Abstractions;

public interface IRelayConfigurationStore
{
    Task<(Uri BaseUrl, string TerminalSerial)?> GetAsync();
    Task SetAsync(Uri baseUrl, string terminalSerial);
}
