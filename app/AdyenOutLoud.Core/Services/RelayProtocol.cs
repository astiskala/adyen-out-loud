using System.Text.Json;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

public static class RelayProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string CreateAck(string eventId) => JsonSerializer.Serialize(
        new { type = "ack", id = eventId }, JsonOptions);

    public static bool TryParsePayment(string json, out PaymentMessage? payment)
    {
        payment = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("protocol", out var protocol) || protocol.ValueKind != JsonValueKind.Number ||
                !protocol.TryGetInt32(out var protocolVersion) || protocolVersion != 1 ||
                !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
                !TryString(message, "id", out var id) ||
                !TryString(message, "type", out var type) ||
                !type.Equals("payment_succeeded", StringComparison.Ordinal) ||
                !TryDate(message, "occurredAt", out var occurredAt) ||
                !TryString(message, "terminalId", out var terminalId) ||
                !TryString(message, "transactionId", out var transactionId) ||
                !TryString(message, "pspReference", out var pspReference) ||
                !TryNullableString(message, "paymentMethod", out var paymentMethod) ||
                !TryAmount(message, out var amount))
            {
                return false;
            }

            payment = new PaymentMessage(id, type, occurredAt, terminalId, transactionId, pspReference, paymentMethod, amount);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryDate(JsonElement element, string name, out DateTimeOffset value)
    {
        value = default;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
               property.TryGetDateTimeOffset(out value);
    }

    private static bool TryNullableString(JsonElement element, string name, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryAmount(JsonElement message, out PaymentAmount? amount)
    {
        amount = null;
        if (!message.TryGetProperty("amount", out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Object || !TryString(property, "currency", out var currency) ||
            !property.TryGetProperty("valueMinor", out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var valueMinor) || valueMinor < 0)
        {
            return false;
        }

        amount = new(currency, valueMinor);
        return true;
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
