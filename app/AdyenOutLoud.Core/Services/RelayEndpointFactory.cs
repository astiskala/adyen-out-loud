namespace AdyenOutLoud.Services;

public static class RelayEndpointFactory
{
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
