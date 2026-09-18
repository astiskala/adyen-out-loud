namespace AdyenOutLoud.Models;

/// <summary>
/// Configuration for connecting to the payment relay service.
/// </summary>
/// <param name="BaseUrl">The base HTTPS URL of the relay service (e.g., "https://company.workers.dev/v1/c/token").</param>
/// <param name="TerminalSerial">The terminal serial number this app instance is configured for.</param>
/// <param name="WebSocketUrl">The computed WebSocket URL for the terminal (e.g., "wss://company.workers.dev/v1/c/token/t/serial/ws").</param>
public sealed record RelayConfiguration(Uri BaseUrl, string TerminalSerial, Uri WebSocketUrl);
