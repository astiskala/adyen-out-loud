using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Services;

namespace AdyenOutLoud.Tests;

public sealed class RelayConfigurationServiceTests
{
    [Fact]
    public async Task GetAsyncReturnsNullWhenNothingIsStoredYet()
    {
        var service = new RelayConfigurationService(new Store());
        Assert.Null(await service.GetAsync());
    }

    [Fact]
    public async Task GetAsyncBuildsTheWebSocketUrlFromStoredValues()
    {
        var store = new Store { Saved = (new("https://relay.example.com/v1/c/token"), "324688170") };
        var service = new RelayConfigurationService(store);

        var configuration = await service.GetAsync();

        Assert.NotNull(configuration);
        Assert.Equal("wss://relay.example.com/v1/c/token/t/324688170/ws", configuration!.WebSocketUrl.AbsoluteUri);
    }

    [Fact]
    public async Task SaveAsyncPersistsAndReturnsTheNewConfiguration()
    {
        var store = new Store();
        var service = new RelayConfigurationService(store);

        var configuration = await service.SaveAsync(new("https://relay.example.com/v1/c/token"), "324688170");

        Assert.Equal(("https://relay.example.com/v1/c/token", "324688170"), (store.Saved!.Value.BaseUrl.AbsoluteUri, store.Saved.Value.TerminalSerial));
        Assert.Equal("wss://relay.example.com/v1/c/token/t/324688170/ws", configuration.WebSocketUrl.AbsoluteUri);
    }

    private sealed class Store : IRelayConfigurationStore
    {
        public (Uri BaseUrl, string TerminalSerial)? Saved { get; set; }
        public Task<(Uri BaseUrl, string TerminalSerial)?> GetAsync() => Task.FromResult(Saved);
        public Task SetAsync(Uri baseUrl, string terminalSerial) { Saved = (baseUrl, terminalSerial); return Task.CompletedTask; }
    }
}
