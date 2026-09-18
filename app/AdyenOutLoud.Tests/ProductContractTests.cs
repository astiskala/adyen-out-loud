using System.Text.Json;
using AdyenOutLoud.Models;
using AdyenOutLoud.Services;

namespace AdyenOutLoud.Tests;

public sealed class ProductContractTests
{
    private const string CompleteEnvelope = """
        {"protocol":1,"message":{"id":"event-1","type":"payment_succeeded","occurredAt":"2026-09-18T12:00:00Z","terminalId":"P400Plus-123","transactionId":"txn-1","pspReference":"PSP-1","paymentMethod":"visa","amount":{"currency":"SGD","valueMinor":1050}}}
        """;

    [Fact]
    public void HttpsRelayBaseBuildsExactWebhookUrl()
    {
        var identity = RelayEndpointFactory.Create(new("https://relay.example.com"), "abc_DEF-123");
        Assert.Equal("https://relay.example.com/v1/i/abc_DEF-123", identity.WebhookUrl.AbsoluteUri);
    }

    [Fact]
    public void HttpsRelayBaseBuildsExactSecureWebSocketUrl()
    {
        var identity = RelayEndpointFactory.Create(new("https://relay.example.com:8443"), "token");
        Assert.Equal("wss://relay.example.com:8443/v1/i/token/ws", identity.WebSocketUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://relay.example.com")]
    [InlineData("wss://relay.example.com")]
    [InlineData("https://relay.example.com/base")]
    [InlineData("https://relay.example.com?x=1")]
    public void RelayBaseRejectsAnythingExceptAnHttpsOrigin(string value) =>
        Assert.Throws<ArgumentException>(() => RelayEndpointFactory.Create(new(value), "token"));

    [Fact]
    public void InstanceTokenUsesExactly32RandomBytesEncodedAsBase64Url()
    {
        var token = DeviceIdentityGenerator.Create();
        Assert.Equal(43, token.Length);
        Assert.DoesNotContain("=", token);
        Assert.DoesNotContain("+", token);
        Assert.DoesNotContain("/", token);
        Assert.Equal(32, Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=").Length);
    }

    [Fact]
    public void ProtocolOnePaymentSucceededEnvelopeParsesEveryField()
    {
        Assert.True(RelayProtocol.TryParsePayment(CompleteEnvelope, out var message));
        Assert.NotNull(message);
        Assert.Equal("event-1", message.Id);
        Assert.Equal("payment_succeeded", message.Type);
        Assert.Equal("P400Plus-123", message.TerminalId);
        Assert.Equal("txn-1", message.TransactionId);
        Assert.Equal("PSP-1", message.PspReference);
        Assert.Equal("visa", message.PaymentMethod);
        Assert.Equal(new PaymentAmount("SGD", 1050), message.Amount);
    }

    [Fact]
    public void NullPaymentMethodIsAccepted()
    {
        var json = CompleteEnvelope.Replace("\"visa\"", "null", StringComparison.Ordinal);
        Assert.True(RelayProtocol.TryParsePayment(json, out var message));
        Assert.Null(message!.PaymentMethod);
    }

    [Fact]
    public void NullAmountIsAccepted()
    {
        var json = CompleteEnvelope.Replace("{\"currency\":\"SGD\",\"valueMinor\":1050}", "null", StringComparison.Ordinal);
        Assert.True(RelayProtocol.TryParsePayment(json, out var message));
        Assert.Null(message!.Amount);
    }

    [Fact]
    public void UnsupportedProtocolVersionIsRejected()
    {
        Assert.False(RelayProtocol.TryParsePayment(CompleteEnvelope.Replace("\"protocol\":1", "\"protocol\":2", StringComparison.Ordinal), out _));
    }

    [Fact]
    public void AnyMessageTypeOtherThanPaymentSucceededIsRejected()
    {
        Assert.False(RelayProtocol.TryParsePayment(CompleteEnvelope.Replace("payment_succeeded", "payment_failed", StringComparison.Ordinal), out _));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void MalformedOrIncompleteEnvelopeIsRejectedWithoutThrowing(string json) =>
        Assert.False(RelayProtocol.TryParsePayment(json, out _));

    public static TheoryData<string, string> RejectedMutations()
    {
        var data = new TheoryData<string, string>();
        void Add(string name, string json) => data.Add(name, json);

        Add("protocol as string", CompleteEnvelope.Replace("\"protocol\":1", "\"protocol\":\"1\"", StringComparison.Ordinal));
        Add("protocol as non-integer number", CompleteEnvelope.Replace("\"protocol\":1", "\"protocol\":1.5", StringComparison.Ordinal));
        Add("message missing entirely", CompleteEnvelope.Replace("\"message\":", "\"msg\":", StringComparison.Ordinal));
        Add("message as an array", """{"protocol":1,"message":[]}""");
        Add("id missing", CompleteEnvelope.Replace("\"id\":\"event-1\",", string.Empty, StringComparison.Ordinal));
        Add("id is whitespace", CompleteEnvelope.Replace("\"event-1\"", "\"   \"", StringComparison.Ordinal));
        Add("id is a number", CompleteEnvelope.Replace("\"id\":\"event-1\"", "\"id\":1", StringComparison.Ordinal));
        Add("type missing", CompleteEnvelope.Replace("\"type\":\"payment_succeeded\",", string.Empty, StringComparison.Ordinal));
        Add("occurredAt missing", CompleteEnvelope.Replace("\"occurredAt\":\"2026-09-18T12:00:00Z\",", string.Empty, StringComparison.Ordinal));
        Add("occurredAt is not a valid date", CompleteEnvelope.Replace("2026-09-18T12:00:00Z", "not-a-date", StringComparison.Ordinal));
        Add("occurredAt is a number", CompleteEnvelope.Replace("\"occurredAt\":\"2026-09-18T12:00:00Z\"", "\"occurredAt\":1", StringComparison.Ordinal));
        Add("terminalId missing", CompleteEnvelope.Replace("\"terminalId\":\"P400Plus-123\",", string.Empty, StringComparison.Ordinal));
        Add("transactionId missing", CompleteEnvelope.Replace("\"transactionId\":\"txn-1\",", string.Empty, StringComparison.Ordinal));
        Add("pspReference missing", CompleteEnvelope.Replace("\"pspReference\":\"PSP-1\",", string.Empty, StringComparison.Ordinal));
        Add("paymentMethod key missing entirely", CompleteEnvelope.Replace("\"paymentMethod\":\"visa\",", string.Empty, StringComparison.Ordinal));
        Add("paymentMethod is a number", CompleteEnvelope.Replace("\"paymentMethod\":\"visa\"", "\"paymentMethod\":7", StringComparison.Ordinal));
        Add("paymentMethod is whitespace", CompleteEnvelope.Replace("\"visa\"", "\"   \"", StringComparison.Ordinal));
        Add("amount key missing entirely", CompleteEnvelope.Replace(",\"amount\":{\"currency\":\"SGD\",\"valueMinor\":1050}", string.Empty, StringComparison.Ordinal));
        Add("amount is an array", CompleteEnvelope.Replace("{\"currency\":\"SGD\",\"valueMinor\":1050}", "[]", StringComparison.Ordinal));
        Add("amount missing currency", CompleteEnvelope.Replace("\"currency\":\"SGD\",", string.Empty, StringComparison.Ordinal));
        Add("amount missing valueMinor", CompleteEnvelope.Replace(",\"valueMinor\":1050", string.Empty, StringComparison.Ordinal));
        Add("amount valueMinor is a string", CompleteEnvelope.Replace("\"valueMinor\":1050", "\"valueMinor\":\"1050\"", StringComparison.Ordinal));
        Add("amount valueMinor is fractional", CompleteEnvelope.Replace("\"valueMinor\":1050", "\"valueMinor\":10.5", StringComparison.Ordinal));
        Add("amount valueMinor is negative", CompleteEnvelope.Replace("\"valueMinor\":1050", "\"valueMinor\":-1", StringComparison.Ordinal));

        return data;
    }

    [Theory]
    [MemberData(nameof(RejectedMutations))]
    public void EveryRequiredFieldMutationIsRejectedWithoutThrowing(string mutation, string json)
    {
        Assert.False(RelayProtocol.TryParsePayment(json, out var message), $"Expected rejection for: {mutation}");
        Assert.Null(message);
    }

    [Fact]
    public void AckContainsExactlyTypeAndId()
    {
        var json = RelayProtocol.CreateAck("event-1");
        Assert.Equal("{\"type\":\"ack\",\"id\":\"event-1\"}", json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.EnumerateObject().Count());
    }
}
