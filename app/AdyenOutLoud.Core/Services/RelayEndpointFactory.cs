using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

public static class RelayEndpointFactory
{
    public static InstanceIdentity Create(Uri relayBaseUrl, string token)
    {
        ArgumentNullException.ThrowIfNull(relayBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (!relayBaseUrl.IsAbsoluteUri || relayBaseUrl.Scheme != Uri.UriSchemeHttps ||
            relayBaseUrl.AbsolutePath != "/" || !string.IsNullOrEmpty(relayBaseUrl.Query) ||
            !string.IsNullOrEmpty(relayBaseUrl.Fragment))
        {
            throw new ArgumentException("RelayBaseUrl must be an HTTPS origin without a path, query, or fragment.", nameof(relayBaseUrl));
        }

        var escapedToken = Uri.EscapeDataString(token);
        var webhookBuilder = new UriBuilder(relayBaseUrl) { Path = $"v1/i/{escapedToken}" };
        var socketBuilder = new UriBuilder(relayBaseUrl)
        {
            Scheme = "wss",
            Port = relayBaseUrl.IsDefaultPort ? -1 : relayBaseUrl.Port,
            Path = $"v1/i/{escapedToken}/ws"
        };
        return new(token, webhookBuilder.Uri, socketBuilder.Uri);
    }
}
