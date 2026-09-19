using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace AdyenOutLoud.UITests;

/// <summary>
/// Terminates TLS in front of the plain-http local Worker, because the app only accepts <c>https://</c> relay URLs.
/// It forwards raw bytes, so HTTP requests and the WebSocket upgrade pass through unchanged.
/// </summary>
internal sealed class TlsProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly X509Certificate2 _certificate;
    private readonly Uri _target;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accepting;

    public TlsProxy(X509Certificate2 certificate, Uri target)
    {
        _certificate = certificate;
        _target = target;
        _listener.Start();
        _accepting = AcceptAsync();
    }

    public Uri BaseUri => new($"https://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try { await _accepting; }
        catch (OperationCanceledException) { }
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException) { return; }

            _ = Task.Run(() => RelayAsync(client));
        }
    }

    private async Task RelayAsync(TcpClient client)
    {
        try
        {
            using (client)
            {
                await using var secure = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                await secure.AuthenticateAsServerAsync(_certificate, clientCertificateRequired: false, SslProtocols.Tls12 | SslProtocols.Tls13, checkCertificateRevocation: false);

                using var upstream = new TcpClient();
                await upstream.ConnectAsync(_target.Host, _target.Port, _stop.Token);
                var upstreamStream = upstream.GetStream();
                var toWorker = secure.CopyToAsync(upstreamStream, _stop.Token);
                var toApp = upstreamStream.CopyToAsync(secure, _stop.Token);
                await Task.WhenAny(toWorker, toApp);
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or AuthenticationException or OperationCanceledException or ObjectDisposedException)
        {
            // A dropped or rejected connection ends this relay only; the test observes the effect on the app.
        }
    }
}
