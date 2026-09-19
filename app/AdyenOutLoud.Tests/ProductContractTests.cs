using AdyenOutLoud.Models;
using AdyenOutLoud.Services;

namespace AdyenOutLoud.Tests;

public sealed class ProductContractTests
{
    private const string CompleteEnvelope = """
        {"protocol":2,"message":{"id":"event-1","type":"payment_succeeded","occurredAt":"2026-09-18T12:00:00Z","terminalId":"P400Plus-123","transactionId":"txn-1","pspReference":"PSP-1"}}
        """;

    [Fact]
    public void CompanyUrlWithTerminalSerialBuildsExactSecureWebSocketUrl()
    {
        var url = RelayEndpointFactory.CreateWebSocketUrl(
            new("https://relay.example.com/v1/c/abc_DEF-123"), "324688170");
        Assert.Equal("wss://relay.example.com/v1/c/abc_DEF-123/t/324688170/ws", url.AbsoluteUri);
    }

    [Fact]
    public void CompanyUrlWithNonDefaultPortIsPreserved()
    {
        var url = RelayEndpointFactory.CreateWebSocketUrl(
            new("https://relay.example.com:8443/v1/c/token"), "324688170");
        Assert.Equal("wss://relay.example.com:8443/v1/c/token/t/324688170/ws", url.AbsoluteUri);
    }

    [Fact]
    public void TerminalSerialIsUrlEscaped()
    {
        var url = RelayEndpointFactory.CreateWebSocketUrl(
            new("https://relay.example.com/v1/c/token"), "has space");
        Assert.Equal("wss://relay.example.com/v1/c/token/t/has%20space/ws", url.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://relay.example.com/v1/c/token")]
    [InlineData("wss://relay.example.com/v1/c/token")]
    [InlineData("https://relay.example.com/v1/c/token?x=1")]
    public void CompanyUrlRejectsAnythingExceptAnHttpsAddress(string value) =>
        Assert.Throws<ArgumentException>(() => RelayEndpointFactory.CreateWebSocketUrl(new(value), "324688170"));

    [Fact]
    public void EmptyTerminalSerialIsRejected() =>
        Assert.Throws<ArgumentException>(() => RelayEndpointFactory.CreateWebSocketUrl(new("https://relay.example.com/v1/c/token"), ""));

    [Fact]
    public void ProtocolTwoPaymentSucceededEnvelopeParsesEveryField()
    {
        Assert.True(RelayConnectionService.TryParsePayment(CompleteEnvelope, out var message));
        Assert.NotNull(message);
        Assert.Equal("event-1", message.Id);
        Assert.Equal("payment_succeeded", message.Type);
        Assert.Equal("P400Plus-123", message.TerminalId);
        Assert.Equal("txn-1", message.TransactionId);
        Assert.Equal("PSP-1", message.PspReference);
    }

    [Fact]
    public void UnsupportedProtocolVersionIsRejected()
    {
        Assert.False(RelayConnectionService.TryParsePayment(CompleteEnvelope.Replace("\"protocol\":2", "\"protocol\":1", StringComparison.Ordinal), out _));
    }

    [Fact]
    public void AnyMessageTypeOtherThanPaymentSucceededIsRejected()
    {
        Assert.False(RelayConnectionService.TryParsePayment(CompleteEnvelope.Replace("payment_succeeded", "payment_failed", StringComparison.Ordinal), out _));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void MalformedOrIncompleteEnvelopeIsRejectedWithoutThrowing(string json) =>
        Assert.False(RelayConnectionService.TryParsePayment(json, out _));

    public static TheoryData<string, string> RejectedMutations()
    {
        var data = new TheoryData<string, string>();
        void Add(string name, string json) => data.Add(name, json);

        Add("protocol as string", CompleteEnvelope.Replace("\"protocol\":2", "\"protocol\":\"2\"", StringComparison.Ordinal));
        Add("protocol as non-integer number", CompleteEnvelope.Replace("\"protocol\":2", "\"protocol\":2.5", StringComparison.Ordinal));
        Add("message missing entirely", CompleteEnvelope.Replace("\"message\":", "\"msg\":", StringComparison.Ordinal));
        Add("message as an array", """{"protocol":2,"message":[]}""");
        Add("id missing", CompleteEnvelope.Replace("\"id\":\"event-1\",", string.Empty, StringComparison.Ordinal));
        Add("id is whitespace", CompleteEnvelope.Replace("\"event-1\"", "\"   \"", StringComparison.Ordinal));
        Add("id is a number", CompleteEnvelope.Replace("\"id\":\"event-1\"", "\"id\":1", StringComparison.Ordinal));
        Add("type missing", CompleteEnvelope.Replace("\"type\":\"payment_succeeded\",", string.Empty, StringComparison.Ordinal));
        Add("occurredAt missing", CompleteEnvelope.Replace("\"occurredAt\":\"2026-09-18T12:00:00Z\",", string.Empty, StringComparison.Ordinal));
        Add("occurredAt is not a valid date", CompleteEnvelope.Replace("2026-09-18T12:00:00Z", "not-a-date", StringComparison.Ordinal));
        Add("occurredAt is a number", CompleteEnvelope.Replace("\"occurredAt\":\"2026-09-18T12:00:00Z\"", "\"occurredAt\":1", StringComparison.Ordinal));
        Add("terminalId missing", CompleteEnvelope.Replace("\"terminalId\":\"P400Plus-123\",", string.Empty, StringComparison.Ordinal));
        Add("transactionId missing", CompleteEnvelope.Replace("\"transactionId\":\"txn-1\",", string.Empty, StringComparison.Ordinal));
        Add("pspReference missing", CompleteEnvelope.Replace(",\"pspReference\":\"PSP-1\"", string.Empty, StringComparison.Ordinal));

        return data;
    }

    [Theory]
    [MemberData(nameof(RejectedMutations))]
    public void EveryRequiredFieldMutationIsRejectedWithoutThrowing(string mutation, string json)
    {
        Assert.False(RelayConnectionService.TryParsePayment(json, out var message), $"Expected rejection for: {mutation}");
        Assert.Null(message);
    }
}
