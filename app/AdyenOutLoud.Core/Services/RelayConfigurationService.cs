using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

/// <summary>
/// Pairs this device with a terminal through the relay's <c>POST /pair/&lt;serial&gt;</c> and keeps the resulting token.
/// </summary>
/// <param name="store">Where the terminal serial and device token are persisted.</param>
/// <param name="relayUrl">The base HTTPS URL of the relay; the same for every installation.</param>
/// <param name="http">The client used for the pairing request.</param>
public sealed partial class RelayConfigurationService(IRelayConfigurationStore store, Uri relayUrl, HttpClient http) : IRelayConfigurationService
{
    /// <summary>How many recent receipts must be quoted; matches the relay's rule.</summary>
    public const int ReceiptsRequired = 2;

    /// <inheritdoc />
    public async Task<RelayConfiguration?> GetAsync(CancellationToken ct = default)
    {
        var pairing = await store.GetAsync().ConfigureAwait(false);
        return pairing is null ? null : Build(pairing);
    }

    /// <inheritdoc />
    public async Task<RelayConfiguration> PairAsync(string terminalSerial, IReadOnlyList<string> receiptCodes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(terminalSerial);
        ArgumentNullException.ThrowIfNull(receiptCodes);
        var serial = terminalSerial.Trim();
        if (!SerialPattern().IsMatch(serial))
            throw new RelayPairingException("Enter this device's terminal serial number (letters, digits, '-' or '_').");
        var codes = receiptCodes.Select(c => (c ?? "").Trim().ToUpperInvariant()).ToList();
        if (codes.Count != ReceiptsRequired || !codes.All(ReceiptCodePattern().IsMatch))
            throw new RelayPairingException("Enter the last 4 characters of the PSP reference from each of two recent receipts.");

        var token = await RequestTokenAsync(serial, codes, ct).ConfigureAwait(false);
        var pairing = new RelayPairing(serial, token);
        var configuration = Build(pairing);
        await store.SetAsync(pairing).ConfigureAwait(false);
        return configuration;
    }

    private async Task<string> RequestTokenAsync(string serial, IReadOnlyList<string> codes, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, IReadOnlyList<string>> { ["receipts"] = codes }), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(RelayEndpointFactory.CreatePairingUrl(relayUrl, serial), content, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new RelayPairingException("Could not reach the relay. Check the internet connection and try again.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new RelayPairingException("The relay did not respond. Try again.", ex);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return ParseToken(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false))
                        ?? throw new RelayPairingException("The relay sent an unexpected response. Try again.");
                case HttpStatusCode.Forbidden:
                    throw new RelayPairingException(
                        "Those codes don't match two approved payments on this terminal in the last 15 minutes. Check the serial number and receipts, or take two new payments and try again.");
                case HttpStatusCode.TooManyRequests:
                    var wait = response.Headers.RetryAfter?.Delta is { } d ? $" in {Math.Max(1, (int)Math.Ceiling(d.TotalMinutes))} minutes" : " later";
                    throw new RelayPairingException($"Too many attempts for this terminal. Try again{wait}.");
                default:
                    throw new RelayPairingException($"The relay could not pair this device (HTTP {(int)response.StatusCode}).");
            }
        }
    }

    // The response is untrusted input: walk it field by field instead of deserializing into a type.
    private static string? ParseToken(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String &&
                   t.GetString() is { } token && TokenPattern().IsMatch(token)
                ? token
                : null;
        }
        catch (JsonException) { return null; }
    }

    private RelayConfiguration Build(RelayPairing pairing) =>
        new(pairing.TerminalSerial, RelayEndpointFactory.CreateWebSocketUrl(relayUrl, pairing.TerminalSerial), pairing.AccessToken);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex SerialPattern();

    [GeneratedRegex("^[A-Z0-9]{4}$")]
    private static partial Regex ReceiptCodePattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$")]
    private static partial Regex TokenPattern();
}
