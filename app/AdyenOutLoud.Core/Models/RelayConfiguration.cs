namespace AdyenOutLoud.Models;

/// <summary>
/// Configuration for connecting to the payment relay service.
/// </summary>
/// <param name="TerminalSerial">The terminal serial number this app instance is paired with.</param>
/// <param name="WebSocketUrl">The WebSocket URL for the terminal (e.g., "wss://adyenoutloud.adam-eea.workers.dev/ws/324688170").</param>
/// <param name="AccessToken">The device token the relay issued when this device was paired.</param>
public sealed record RelayConfiguration(string TerminalSerial, Uri WebSocketUrl, string AccessToken);

/// <summary>
/// What is persisted after pairing: the terminal and the device token the relay issued for it.
/// </summary>
/// <param name="TerminalSerial">The terminal serial number.</param>
/// <param name="AccessToken">The device token.</param>
public sealed record RelayPairing(string TerminalSerial, string AccessToken);

/// <summary>
/// Pairing failed; <see cref="Exception.Message"/> is written for the person setting up the device.
/// </summary>
public sealed class RelayPairingException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="RelayPairingException"/> class.</summary>
    public RelayPairingException() { }

    /// <summary>Initializes a new instance of the <see cref="RelayPairingException"/> class.</summary>
    /// <param name="message">A message for the person setting up the device.</param>
    public RelayPairingException(string message) : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="RelayPairingException"/> class.</summary>
    /// <param name="message">A message for the person setting up the device.</param>
    /// <param name="innerException">The underlying failure.</param>
    public RelayPairingException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// The relay refused the device token (HTTP 401): the device must be paired again.
/// </summary>
public sealed class RelayUnauthorizedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="RelayUnauthorizedException"/> class.</summary>
    public RelayUnauthorizedException() : base("The relay did not accept this device's pairing.") { }

    /// <summary>Initializes a new instance of the <see cref="RelayUnauthorizedException"/> class.</summary>
    /// <param name="message">Detail about the refusal.</param>
    public RelayUnauthorizedException(string message) : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="RelayUnauthorizedException"/> class.</summary>
    /// <param name="message">Detail about the refusal.</param>
    /// <param name="innerException">The underlying failure.</param>
    public RelayUnauthorizedException(string message, Exception innerException) : base(message, innerException) { }
}
