using System.Net.WebSockets;
using System.Text;
using AdyenOutLoud.Abstractions;

namespace AdyenOutLoud.Services;

/// <summary>
/// Factory for creating <see cref="ClientWebSocketConnection"/> instances.
/// </summary>
public sealed class ClientWebSocketConnectionFactory : IRelayConnectionFactory
{
    /// <inheritdoc />
    public IRelayConnection Create() => new ClientWebSocketConnection();
}

/// <summary>
/// Client WebSocket connection to the relay service.
/// </summary>
public sealed class ClientWebSocketConnection : IRelayConnection
{
    private const int MaxMessageBytes = 64 * 1024;
    private readonly ClientWebSocket _socket = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientWebSocketConnection"/> class.
    /// </summary>
    public ClientWebSocketConnection()
    {
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
    }

    /// <inheritdoc />
    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
        _socket.ConnectAsync(uri, cancellationToken);

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "App moved to background", cancellationToken);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
