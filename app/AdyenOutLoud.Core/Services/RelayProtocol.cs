using System.Text.Json;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

/// <summary>
/// Parses and validates the relay protocol envelope.
/// </summary>
public static class RelayProtocol
{
    /// <summary>
    /// Attempts to parse a JSON string as a payment success envelope.
    /// </summary>
    /// <param name="json">The JSON string to parse.</param>
    /// <param name="payment">The parsed payment message, if successful.</param>
    /// <returns>True if the JSON is a valid payment success envelope; otherwise, false.</returns>
    public static bool TryParsePayment(string json, out PaymentMessage? payment)
    {
        payment = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("protocol", out var protocol) || protocol.ValueKind != JsonValueKind.Number ||
                !protocol.TryGetInt32(out var protocolVersion) || protocolVersion != 2 ||
                !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
                !TryString(message, "id", out var id) ||
                !TryString(message, "type", out var type) ||
                !type.Equals("payment_succeeded", StringComparison.Ordinal) ||
                !TryDate(message, "occurredAt", out var occurredAt) ||
                !TryString(message, "terminalId", out var terminalId) ||
                !TryString(message, "transactionId", out var transactionId) ||
                !TryString(message, "pspReference", out var pspReference))
            {
                return false;
            }

            payment = new PaymentMessage(id, type, occurredAt, terminalId, transactionId, pspReference);
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
