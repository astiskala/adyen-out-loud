using System.Net.WebSockets;
using System.Text;
using AdyenOutLoud.Abstractions;

namespace AdyenOutLoud.Services;

public sealed class ClientWebSocketConnectionFactory : IRelayConnectionFactory
{
    public IRelayConnection Create() => new ClientWebSocketConnection();
}

public sealed class ClientWebSocketConnection : IRelayConnection
{
    private const int MaxMessageBytes = 64 * 1024;
    private readonly ClientWebSocket _socket = new();

    public ClientWebSocketConnection()
    {
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
    }

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
        _socket.ConnectAsync(uri, cancellationToken);

    public Task SendAsync(string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        return _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new WebSocketException("The relay sent a non-text message.");
            }

            if (message.Length + result.Count > MaxMessageBytes)
            {
                throw new WebSocketException("The relay message exceeded 64 KiB.");
            }

            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
            }
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "App moved to background", cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
