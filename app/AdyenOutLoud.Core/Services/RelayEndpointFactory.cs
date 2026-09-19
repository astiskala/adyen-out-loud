namespace AdyenOutLoud.Services;

/// <summary>
/// Knows where the shared relay lives and builds per-terminal WebSocket URLs from it.
/// </summary>
public static class RelayEndpointFactory
{
    /// <summary>
    /// The hosted relay every installation talks to. The same host receives Adyen's Display webhook at <c>/webhook</c>.
    /// </summary>
    public static readonly Uri DefaultBaseUrl = new("https://adyenoutloud.adam-eea.workers.dev");

    /// <summary>
    /// Creates the WebSocket URL for a specific terminal.
    /// </summary>
    /// <param name="relayUrl">The base HTTPS URL of the relay (no query/fragment).</param>
    /// <param name="terminalSerial">The terminal serial number.</param>
    /// <returns>A WSS WebSocket URL for the terminal.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="relayUrl"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="relayUrl"/> is not a valid HTTPS URL or <paramref name="terminalSerial"/> is empty.</exception>
    public static Uri CreateWebSocketUrl(Uri relayUrl, string terminalSerial)
    {
        ArgumentNullException.ThrowIfNull(relayUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalSerial);
        if (!relayUrl.IsAbsoluteUri || relayUrl.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(relayUrl.Query) || !string.IsNullOrEmpty(relayUrl.Fragment))
        {
            throw new ArgumentException("The relay URL must be an HTTPS address with no query or fragment.", nameof(relayUrl));
        }

        var builder = new UriBuilder(relayUrl)
        {
            Scheme = "wss",
            Port = relayUrl.IsDefaultPort ? -1 : relayUrl.Port,
            Path = $"{relayUrl.AbsolutePath.TrimEnd('/')}/ws/{Uri.EscapeDataString(terminalSerial)}",
        };
        return builder.Uri;
    }
}
