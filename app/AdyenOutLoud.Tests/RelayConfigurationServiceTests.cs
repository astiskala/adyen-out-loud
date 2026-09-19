using System.Net;
using System.Net.Http.Headers;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;
using AdyenOutLoud.Services;

namespace AdyenOutLoud.Tests;

public sealed class RelayConfigurationServiceTests
{
    private static readonly Uri RelayUrl = new("https://relay.example.com");
    private const string Token = "abcDEF123_-abcDEF123_-abcDEF123_-abcDEF123_";

    [Fact]
    public async Task GetAsyncReturnsNullWhenNothingIsStoredYet()
    {
        var service = new RelayConfigurationService(new Store(), RelayUrl, new HttpClient(new Relay()));
        Assert.Null(await service.GetAsync());
    }

    [Fact]
    public async Task GetAsyncBuildsTheWebSocketUrlFromTheStoredPairing()
    {
        var service = new RelayConfigurationService(new Store { Saved = new("324688170", Token) }, RelayUrl, new HttpClient(new Relay()));

        var configuration = await service.GetAsync();

        Assert.NotNull(configuration);
        Assert.Equal("324688170", configuration!.TerminalSerial);
        Assert.Equal("wss://relay.example.com/ws/324688170", configuration.WebSocketUrl.AbsoluteUri);
        Assert.Equal(Token, configuration.AccessToken);
    }

    [Fact]
    public async Task PairAsyncPostsTheReceiptCodesAndSavesTheIssuedToken()
    {
        var store = new Store();
        var relay = new Relay(HttpStatusCode.OK, $$"""{"token":"{{Token}}"}""");
        var service = new RelayConfigurationService(store, RelayUrl, new HttpClient(relay));

        var configuration = await service.PairAsync(" 324688170 ", ["ab12", " CD34 "]);

        Assert.Equal(HttpMethod.Post, relay.Method);
        Assert.Equal("https://relay.example.com/pair/324688170", relay.Uri!.AbsoluteUri);
        Assert.Equal("application/json", relay.ContentType);
        Assert.Equal("""{"receipts":["AB12","CD34"]}""", relay.Body);
        Assert.Equal(new RelayPairing("324688170", Token), store.Saved);
        Assert.Equal(Token, configuration.AccessToken);
        Assert.Equal("wss://relay.example.com/ws/324688170", configuration.WebSocketUrl.AbsoluteUri);
    }

    public static TheoryData<string, string[]> InvalidInput { get; } = new()
    {
        { "", ["AB12", "CD34"] },
        { "bad serial", ["AB12", "CD34"] },
        { "324688170", ["AB12"] },
        { "324688170", ["AB12", "CD34", "EF56"] },
        { "324688170", ["AB12", "CD3"] },
        { "324688170", ["AB12", "CD-4"] },
    };

    [Theory]
    [MemberData(nameof(InvalidInput))]
    public async Task PairAsyncRejectsInvalidInputWithoutCallingTheRelay(string serial, string[] codes)
    {
        var relay = new Relay();
        var service = new RelayConfigurationService(new Store(), RelayUrl, new HttpClient(relay));

        await Assert.ThrowsAsync<RelayPairingException>(() => service.PairAsync(serial, codes));

        Assert.Null(relay.Uri);
    }

    public static TheoryData<HttpStatusCode, string, string> RelayRefusals { get; } = new()
    {
        { HttpStatusCode.Forbidden, "{}", "don't match" },
        { HttpStatusCode.TooManyRequests, "{}", "Try again in 3 minutes" },
        { HttpStatusCode.BadRequest, "{}", "HTTP 400" },
        { HttpStatusCode.OK, "not json", "unexpected response" },
        { HttpStatusCode.OK, """{"token":42}""", "unexpected response" },
        { HttpStatusCode.OK, """{"token":"has spaces"}""", "unexpected response" },
        { HttpStatusCode.OK, "[]", "unexpected response" },
    };

    [Theory]
    [MemberData(nameof(RelayRefusals))]
    public async Task PairAsyncExplainsARefusalAndSavesNothing(HttpStatusCode status, string body, string expected)
    {
        var store = new Store();
        var relay = new Relay(status, body) { RetryAfter = TimeSpan.FromSeconds(150) };
        var service = new RelayConfigurationService(store, RelayUrl, new HttpClient(relay));

        var error = await Assert.ThrowsAsync<RelayPairingException>(() => service.PairAsync("324688170", ["AB12", "CD34"]));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task PairAsyncExplainsAnUnreachableRelay()
    {
        var relay = new Relay { Failure = new HttpRequestException("offline") };
        var service = new RelayConfigurationService(new Store(), RelayUrl, new HttpClient(relay));

        var error = await Assert.ThrowsAsync<RelayPairingException>(() => service.PairAsync("324688170", ["AB12", "CD34"]));

        Assert.Contains("Could not reach the relay", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PairAsyncExplainsARelayThatTimesOut()
    {
        var relay = new Relay { Failure = new TaskCanceledException("timeout") };
        var service = new RelayConfigurationService(new Store(), RelayUrl, new HttpClient(relay));

        var error = await Assert.ThrowsAsync<RelayPairingException>(() => service.PairAsync("324688170", ["AB12", "CD34"]));

        Assert.Contains("did not respond", error.Message, StringComparison.Ordinal);
    }

    private sealed class Store : IRelayConfigurationStore
    {
        public RelayPairing? Saved { get; set; }
        public Task<RelayPairing?> GetAsync() => Task.FromResult(Saved);
        public Task SetAsync(RelayPairing pairing) { Saved = pairing; return Task.CompletedTask; }
    }

    private sealed class Relay(HttpStatusCode status = HttpStatusCode.OK, string body = "{}") : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string? ContentType { get; private set; }
        public string? Body { get; private set; }
        public TimeSpan? RetryAfter { get; init; }
        public Exception? Failure { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Failure is not null) throw Failure;
            Method = request.Method;
            Uri = request.RequestUri;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
            if (RetryAfter is { } delay) response.Headers.RetryAfter = new RetryConditionHeaderValue(delay);
            return response;
        }
    }
}
