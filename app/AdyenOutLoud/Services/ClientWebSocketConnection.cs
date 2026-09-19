using System.Net;
using System.Net.WebSockets;
using System.Text;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

/// <summary>Creates <see cref="ClientWebSocketConnection"/>s.</summary>
public sealed class ClientWebSocketConnectionFactory : IRelayConnectionFactory
{
    /// <inheritdoc />
    public IRelayConnection Create() => new ClientWebSocketConnection();
}

/// <summary>Relay connection over <see cref="ClientWebSocket"/>: text frames only, at most 64 KiB each.</summary>
public sealed class ClientWebSocketConnection : IRelayConnection
{
    private const int MaxBytes = 64 * 1024;
    private readonly ClientWebSocket _ws = new() { Options = { KeepAliveInterval = TimeSpan.FromSeconds(20), CollectHttpResponseDetails = true } };

    /// <inheritdoc />
    public async Task ConnectAsync(Uri uri, string accessToken, CancellationToken ct)
    {
        _ws.Options.SetRequestHeader("Authorization", $"Bearer {accessToken}");
        try { await _ws.ConnectAsync(uri, ct); }
        catch (WebSocketException ex) when (_ws.HttpStatusCode == HttpStatusCode.Unauthorized)
        {
            throw new RelayUnauthorizedException("The relay did not accept this device's pairing.", ex);
        }
    }

    /// <inheritdoc />
    public async Task<string?> ReceiveAsync(CancellationToken ct)
    {
        var buf = new byte[4096];
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await _ws.ReceiveAsync(buf, ct);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            if (r.MessageType != WebSocketMessageType.Text) throw new WebSocketException("Non-text message");
            if (ms.Length + r.Count > MaxBytes) throw new WebSocketException("Message > 64 KiB");
            ms.Write(buf, 0, r.Count);
            if (r.EndOfMessage) return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }
    }

    /// <inheritdoc />
    public Task CloseAsync(CancellationToken ct) => _ws.State is WebSocketState.Open or WebSocketState.CloseReceived
        ? _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "background", ct) : Task.CompletedTask;

    /// <inheritdoc />
    public ValueTask DisposeAsync() { _ws.Dispose(); return ValueTask.CompletedTask; }
}
