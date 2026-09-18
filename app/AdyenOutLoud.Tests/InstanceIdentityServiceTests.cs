using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Services;

namespace AdyenOutLoud.Tests;

public sealed class InstanceIdentityServiceTests
{
    [Fact]
    public async Task ExistingSecureTokenIsReusedWithoutWritingAnotherToken()
    {
        var existingToken = new string('A', 43);
        var store = new TokenStore { Token = existingToken };
        var service = new InstanceIdentityService(new("https://relay.example.com"), store);

        var identity = await service.GetAsync();

        Assert.Equal(existingToken, identity.Token);
        Assert.Equal(0, store.SetCalls);
        Assert.Equal($"https://relay.example.com/v1/i/{existingToken}", identity.WebhookUrl.AbsoluteUri);
    }

    [Fact]
    public async Task MissingSecureTokenGeneratesAndPersistsOneToken()
    {
        var store = new TokenStore();
        var service = new InstanceIdentityService(new("https://relay.example.com"), store);

        var first = await service.GetAsync();
        var second = await service.GetAsync();

        Assert.Equal(43, first.Token.Length);
        Assert.Same(first, second);
        Assert.Equal(first.Token, store.Token);
        Assert.Equal(1, store.SetCalls);
    }

    [Fact]
    public async Task UnreadableSecureTokenIsRemovedAndRegenerated()
    {
        var store = new TokenStore { ThrowOnGet = true };
        var service = new InstanceIdentityService(new("https://relay.example.com"), store);

        var identity = await service.GetAsync();

        Assert.True(store.WasRemoved);
        Assert.Equal(identity.Token, store.Token);
        Assert.Equal(1, store.SetCalls);
    }

    [Fact]
    public async Task MalformedSecureTokenIsRemovedAndRegenerated()
    {
        var store = new TokenStore { Token = "not-base64url" };
        var service = new InstanceIdentityService(new("https://relay.example.com"), store);

        var identity = await service.GetAsync();

        Assert.True(store.WasRemoved);
        Assert.True(DeviceIdentityGenerator.IsValid(identity.Token));
        Assert.Equal(identity.Token, store.Token);
    }

    private sealed class TokenStore : IInstanceTokenStore
    {
        public string? Token { get; set; }
        public bool ThrowOnGet { get; init; }
        public bool WasRemoved { get; private set; }
        public int SetCalls { get; private set; }
        public Task<string?> GetAsync() => ThrowOnGet ? throw new InvalidOperationException("corrupt") : Task.FromResult(Token);
        public Task SetAsync(string token) { Token = token; SetCalls++; return Task.CompletedTask; }
        public void Remove() { WasRemoved = true; Token = null; }
    }
}
