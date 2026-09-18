namespace AdyenOutLoud.Services;

/// <summary>
/// Factory for creating relay WebSocket URLs from base HTTPS URLs.
/// </summary>
public static class RelayEndpointFactory
{
    /// <summary>
    /// Creates a WebSocket URL for a specific terminal from the company base URL.
    /// </summary>
    /// <param name="companyUrl">The base HTTPS URL of the relay (must be HTTPS, no query/fragment).</param>
    /// <param name="terminalSerial">The terminal serial number.</param>
    /// <returns>A WSS WebSocket URL for the terminal.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="companyUrl"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="companyUrl"/> is not a valid HTTPS URL or <paramref name="terminalSerial"/> is empty.</exception>
    public static Uri CreateWebSocketUrl(Uri companyUrl, string terminalSerial)
    {
        ArgumentNullException.ThrowIfNull(companyUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalSerial);
        if (!companyUrl.IsAbsoluteUri || companyUrl.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(companyUrl.Query) || !string.IsNullOrEmpty(companyUrl.Fragment))
        {
            throw new ArgumentException("The relay URL must be an HTTPS address with no query or fragment.", nameof(companyUrl));
        }

        var escapedSerial = Uri.EscapeDataString(terminalSerial);
        var builder = new UriBuilder(companyUrl)
        {
            Scheme = "wss",
            Port = companyUrl.IsDefaultPort ? -1 : companyUrl.Port,
            Path = $"{companyUrl.AbsolutePath.TrimEnd('/')}/t/{escapedSerial}/ws",
        };
        return builder.Uri;
    }
}
