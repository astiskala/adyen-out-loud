using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Services;

namespace AdyenOutLoud.Tests;

public sealed class RelayConfigurationServiceTests
{
    private static readonly Uri Relay = new("https://relay.example.com");

    [Fact]
    public async Task GetAsyncReturnsNullWhenNothingIsStoredYet()
    {
        var service = new RelayConfigurationService(new Store(), Relay);
        Assert.Null(await service.GetAsync());
    }

    [Fact]
    public async Task GetAsyncBuildsTheWebSocketUrlFromTheStoredSerial()
    {
        var service = new RelayConfigurationService(new Store { Saved = "324688170" }, Relay);

        var configuration = await service.GetAsync();

        Assert.NotNull(configuration);
        Assert.Equal("324688170", configuration!.TerminalSerial);
        Assert.Equal("wss://relay.example.com/ws/324688170", configuration.WebSocketUrl.AbsoluteUri);
    }

    [Fact]
    public async Task SaveAsyncPersistsAndReturnsTheNewConfiguration()
    {
        var store = new Store();
        var service = new RelayConfigurationService(store, Relay);

        var configuration = await service.SaveAsync("324688170");

        Assert.Equal("324688170", store.Saved);
        Assert.Equal("wss://relay.example.com/ws/324688170", configuration.WebSocketUrl.AbsoluteUri);
    }

    private sealed class Store : IRelayConfigurationStore
    {
        public string? Saved { get; set; }
        public Task<string?> GetAsync() => Task.FromResult(Saved);
        public Task SetAsync(string terminalSerial) { Saved = terminalSerial; return Task.CompletedTask; }
    }
}
