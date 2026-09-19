namespace AdyenOutLoud.Abstractions;

/// <summary>WebSocket connection to the relay.</summary>
public interface IRelayConnection : IAsyncDisposable
{
    /// <summary>Opens the connection, presenting the device token.</summary>
    /// <param name="uri">The terminal's WebSocket URL.</param>
    /// <param name="accessToken">The device token, sent as <c>Authorization: Bearer</c>.</param>
    /// <param name="ct">Cancels the connection attempt.</param>
    /// <returns>A task that completes once connected.</returns>
    /// <exception cref="Models.RelayUnauthorizedException">The relay refused the token.</exception>
    Task ConnectAsync(Uri uri, string accessToken, CancellationToken ct);

    /// <summary>Receives the next text message, or null when the relay closes the connection.</summary>
    /// <param name="ct">Cancels the receive.</param>
    /// <returns>The message, or null on close.</returns>
    Task<string?> ReceiveAsync(CancellationToken ct);

    /// <summary>Closes the connection normally.</summary>
    /// <param name="ct">Cancels the close handshake.</param>
    /// <returns>A task that completes once closed.</returns>
    Task CloseAsync(CancellationToken ct);
}

/// <summary>Creates relay connections.</summary>
public interface IRelayConnectionFactory
{
    /// <summary>Creates a new, unopened connection.</summary>
    /// <returns>The connection.</returns>
    IRelayConnection Create();
}
