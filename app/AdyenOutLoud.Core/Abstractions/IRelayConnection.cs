namespace AdyenOutLoud.Abstractions;

/// <summary>
/// Represents a WebSocket connection to the relay service.
/// </summary>
public interface IRelayConnection : IAsyncDisposable
{
    /// <summary>
    /// Connects to the relay WebSocket endpoint.
    /// </summary>
    /// <param name="uri">The WebSocket URI to connect to.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous connection.</returns>
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    /// <summary>
    /// Receives the next message from the relay.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The JSON message string, or null if the connection closed cleanly.</returns>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Closes the WebSocket connection gracefully.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous close operation.</returns>
    Task CloseAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Factory for creating relay connections.
/// </summary>
public interface IRelayConnectionFactory
{
    /// <summary>
    /// Creates a new relay connection instance.
    /// </summary>
    /// <returns>A new <see cref="IRelayConnection"/> instance.</returns>
    IRelayConnection Create();
}
