namespace AdyenOutLoud.Models;

/// <summary>
/// Configuration for connecting to the payment relay service.
/// </summary>
/// <param name="TerminalSerial">The terminal serial number this app instance is configured for.</param>
/// <param name="WebSocketUrl">The WebSocket URL for the terminal (e.g., "wss://adyenoutloud.adam-eea.workers.dev/ws/324688170").</param>
public sealed record RelayConfiguration(string TerminalSerial, Uri WebSocketUrl);
