using System.Text.Json;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

/// <summary>Manages WebSocket connection to relay, with reconnection and payment announcement.</summary>
public sealed class RelayConnectionService(
    IRelayConfigurationService configService,
    IRelayConnectionFactory connFactory,
    ISettingsService settings,
    IAnnouncementPlayer player) : IRelayConnectionService, IAsyncDisposable
{
    private const string NotPaired = "Pair this device: enter the terminal serial number and the codes from two recent receipts.";
    private const string NoLongerPaired = "The relay no longer recognises this device. Pair it with the terminal again.";
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private Task? _task;

    /// <inheritdoc />
    public event EventHandler<RelayStatus>? StatusChanged;
    /// <inheritdoc />
    public event EventHandler<string>? Diagnostic;
    /// <inheritdoc />
    public event EventHandler<AnnouncementResult>? AnnouncementCompleted;

    /// <inheritdoc />
    public void Start()
    {
        lock (_sync)
        {
            if (_task is { IsCompleted: false } && _cts is not { IsCancellationRequested: true }) return;
            _cts?.Dispose();
            _cts = new();
            _task = RunAsync(_cts.Token);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        Task? t;
        lock (_sync) { _cts?.Cancel(); t = _task; }
        if (t != null) { try { await t; } catch (OperationCanceledException) { } }
        SetStatus(RelayConnectionState.Connecting, "Paused while the app is in the background.");
    }

    private async Task RunAsync(CancellationToken ct)
    {
        RelayConfiguration cfg;
        try
        {
            var saved = await configService.GetAsync(ct);
            if (saved is null) { SetStatus(RelayConnectionState.NeedsAttention, NotPaired); return; }
            cfg = saved;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { SetStatus(RelayConnectionState.NeedsAttention, "Could not read relay config"); Diagnostic?.Invoke(this, ex.Message); return; }

        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            await using var conn = connFactory.Create();
            try
            {
                SetStatus(RelayConnectionState.Connecting, "Connecting to the relay...");
                await conn.ConnectAsync(cfg.WebSocketUrl, cfg.AccessToken, ct);
                failures = 0;
                SetStatus(RelayConnectionState.Listening, "Connected and waiting for payments.");

                while (!ct.IsCancellationRequested)
                {
                    var json = await conn.ReceiveAsync(ct);
                    if (json is null) throw new IOException("Relay closed connection");
                    if (!TryParsePayment(json, out var pmt) || pmt is null) { Diagnostic?.Invoke(this, "Invalid relay envelope"); continue; }
                    Diagnostic?.Invoke(this, (await AnnounceAsync(pmt, ct)).Detail);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { await TryClose(conn); break; }
            catch (RelayUnauthorizedException) { SetStatus(RelayConnectionState.NeedsAttention, NoLongerPaired); break; }
            catch (Exception ex) when (ex is IOException or System.Net.WebSockets.WebSocketException or TimeoutException)
            {
                failures++;
                var sec = Math.Min(30, Math.Pow(2, Math.Min(failures - 1, 5)));
                SetStatus(RelayConnectionState.Connecting, $"Connection lost. Retrying in {sec:0} seconds.");
                await Task.Delay(TimeSpan.FromSeconds(sec), ct);
            }
            catch (Exception ex) { SetStatus(RelayConnectionState.NeedsAttention, ex.Message); Diagnostic?.Invoke(this, $"Relay stopped: {ex.Message}"); break; }
        }
    }

    /// <inheritdoc />
    public async Task<AnnouncementResult> AnnounceAsync(PaymentMessage message, CancellationToken ct)
    {
        if (!await settings.TryReserveEventIdAsync(message.Id, DateTimeOffset.UtcNow, ct))
            return Complete(new(message.Id, true, false, "Duplicate; not announced again.", message));

        try
        {
            var lang = settings.SelectedLanguage;
            await player.PlayAsync(AnnouncementSound.PaymentReceived, lang, ct);
            return Complete(new(message.Id, false, true, $"Played the {lang.DisplayName} announcement.", message));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { return Complete(new(message.Id, false, false, $"Payment recorded; playback failed: {ex.Message}", message)); }
    }

    private AnnouncementResult Complete(AnnouncementResult r) { try { AnnouncementCompleted?.Invoke(this, r); } catch { } return r; }

    /// <summary>Validates a relay envelope field by field and extracts the payment.</summary>
    /// <param name="json">The raw WebSocket message.</param>
    /// <param name="pmt">The payment, if the envelope is valid.</param>
    /// <returns>True if the envelope is a valid payment message.</returns>
    public static bool TryParsePayment(string json, out PaymentMessage? pmt)
    {
        pmt = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("protocol", out var p) || p.ValueKind != JsonValueKind.Number || !p.TryGetInt32(out var pv) || pv != 2 ||
                !root.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object ||
                !Str(m, "id", out var id) || !Str(m, "type", out var type) || type != "payment_succeeded" ||
                !Date(m, "occurredAt", out var occ) ||
                !Str(m, "terminalId", out var tid) || !Str(m, "transactionId", out var txid) || !Str(m, "pspReference", out var psp))
                return false;
            pmt = new(id, type, occ, tid, txid, psp);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool Date(JsonElement e, string n, out DateTimeOffset v) { v = default; return e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String && p.TryGetDateTimeOffset(out v); }
    private static bool Str(JsonElement e, string n, out string v) { v = ""; return e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v = p.GetString() ?? ""); }

    private static async Task TryClose(IRelayConnection c) { using var t = new CancellationTokenSource(TimeSpan.FromSeconds(1)); try { await c.CloseAsync(t.Token); } catch { /* best effort: the socket is being abandoned anyway */ } }

    private void SetStatus(RelayConnectionState s, string d) => StatusChanged?.Invoke(this, new(s, d));

    /// <inheritdoc />
    public async ValueTask DisposeAsync() { await StopAsync(); _cts?.Dispose(); }
}
