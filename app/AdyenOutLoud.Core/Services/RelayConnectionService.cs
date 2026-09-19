using System.Text.Json;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

/// <summary>
/// Manages the persistent WebSocket connection to the payment relay service.
/// Handles connection lifecycle, reconnection with exponential backoff, and message dispatch.
/// Also handles payment announcement (deduplication, playing the clip).
/// </summary>
public sealed class RelayConnectionService(
    IRelayConfigurationService configurationService,
    IRelayConnectionFactory connectionFactory,
    ISettingsService settings,
    IAnnouncementPlayer player) : IRelayConnectionService, IAsyncDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;

    /// <inheritdoc />
    public event EventHandler<RelayStatus>? StatusChanged;

    /// <inheritdoc />
    public event EventHandler<string>? Diagnostic;

    /// <summary>
    /// Event raised when a payment announcement completes (successfully or not).
    /// </summary>
    public event EventHandler<AnnouncementResult>? AnnouncementCompleted;

    /// <inheritdoc />
    public void Start()
    {
        lock (_sync)
        {
            if (_runTask is { IsCompleted: false })
            {
                if (_runCancellation is not { IsCancellationRequested: true })
                {
                    return;
                }

                var previous = _runTask;
                _runCancellation.Dispose();
                _runCancellation = new CancellationTokenSource();
                _runTask = RestartAfterAsync(previous, _runCancellation.Token);
                return;
            }

            _runCancellation?.Dispose();
            _runCancellation = new CancellationTokenSource();
            _runTask = RunAsync(_runCancellation.Token);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        Task? task;
        lock (_sync)
        {
            _runCancellation?.Cancel();
            task = _runTask;
        }

        if (task is not null)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        SetStatus(RelayConnectionState.Connecting, "Paused while the app is in the background.");
    }

    private async Task RestartAfterAsync(Task previous, CancellationToken cancellationToken)
    {
        try { await previous.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        cancellationToken.ThrowIfCancellationRequested();
        await RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        RelayConfiguration configuration;
        try
        {
            var found = await configurationService.GetAsync(cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                SetStatus(RelayConnectionState.NeedsAttention, "Enter this device's terminal serial number to start listening.");
                return;
            }
            configuration = found;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            SetStatus(RelayConnectionState.NeedsAttention, "Could not read the saved relay configuration.");
            Diagnostic?.Invoke(this, exception.Message);
            return;
        }

        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var connection = connectionFactory.Create();
            try
            {
                SetStatus(RelayConnectionState.Connecting, "Connecting to the relay...");
                await connection.ConnectAsync(configuration.WebSocketUrl, cancellationToken).ConfigureAwait(false);
                failures = 0;
                SetStatus(RelayConnectionState.Listening, "Connected and waiting for payments.");

                while (!cancellationToken.IsCancellationRequested)
                {
                    var json = await connection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                    if (json is null) throw new IOException("The relay closed the connection.");
                    if (!TryParsePayment(json, out var payment) || payment is null)
                    {
                        Diagnostic?.Invoke(this, "Ignored an invalid relay envelope.");
                        continue;
                    }

                    var result = await AnnounceAsync(payment, cancellationToken).ConfigureAwait(false);
                    Diagnostic?.Invoke(this, result.Detail);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await TryCloseAsync(connection).ConfigureAwait(false);
                break;
            }
            catch (Exception exception) when (exception is IOException or System.Net.WebSockets.WebSocketException or TimeoutException)
            {
                failures++;
                var seconds = Math.Min(30, Math.Pow(2, Math.Min(failures - 1, 5)));
                SetStatus(RelayConnectionState.Connecting, $"Connection lost. Retrying in {seconds:0} seconds.");
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                SetStatus(RelayConnectionState.NeedsAttention, exception.Message);
                Diagnostic?.Invoke(this, $"Relay stopped: {exception.Message}");
                break;
            }
        }
    }

    /// <summary>
    /// Announces a successful payment by playing the pre-recorded clip.
    /// </summary>
    /// <param name="message">The payment message to announce.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result of the announcement attempt.</returns>
    public async Task<AnnouncementResult> AnnounceAsync(PaymentMessage message, CancellationToken cancellationToken)
    {
        var isNew = await settings.TryReserveEventIdAsync(message.Id, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        if (!isNew)
        {
            return Complete(new(message.Id, true, false, "Duplicate; not announced again.", message));
        }

        try
        {
            var language = settings.SelectedLanguage;
            await player.PlayAsync(AnnouncementSound.PaymentReceived, language, cancellationToken).ConfigureAwait(false);
            return Complete(new(message.Id, false, true, $"Played the {language.DisplayName} announcement.", message));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Complete(new(message.Id, false, false, $"Payment recorded; playback failed: {exception.Message}", message));
        }
    }

    private AnnouncementResult Complete(AnnouncementResult result)
    {
        try
        {
            AnnouncementCompleted?.Invoke(this, result);
        }
        catch (Exception)
        {
            // UI observers must never prevent processing the next message.
        }
        return result;
    }

    /// <summary>
    /// Attempts to parse a JSON string as a payment success envelope.
    /// </summary>
    /// <param name="json">The JSON string to parse.</param>
    /// <param name="payment">The parsed payment message, if successful.</param>
    /// <returns>True if the JSON is a valid payment success envelope; otherwise, false.</returns>
    public static bool TryParsePayment(string json, out PaymentMessage? payment)
    {
        payment = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("protocol", out var protocol) || protocol.ValueKind != JsonValueKind.Number ||
                !protocol.TryGetInt32(out var protocolVersion) || protocolVersion != 2 ||
                !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
                !TryString(message, "id", out var id) ||
                !TryString(message, "type", out var type) ||
                !type.Equals("payment_succeeded", StringComparison.Ordinal) ||
                !TryDate(message, "occurredAt", out var occurredAt) ||
                !TryString(message, "terminalId", out var terminalId) ||
                !TryString(message, "transactionId", out var transactionId) ||
                !TryString(message, "pspReference", out var pspReference))
            {
                return false;
            }

            payment = new PaymentMessage(id, type, occurredAt, terminalId, transactionId, pspReference);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryDate(JsonElement element, string name, out DateTimeOffset value)
    {
        value = default;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
               property.TryGetDateTimeOffset(out value);
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static async Task TryCloseAsync(IRelayConnection connection)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try { await connection.CloseAsync(timeout.Token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or System.Net.WebSockets.WebSocketException) { }
    }

    private void SetStatus(RelayConnectionState state, string detail) => StatusChanged?.Invoke(this, new(state, detail));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _runCancellation?.Dispose();
    }
}
