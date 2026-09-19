using System.Net.WebSockets;
using System.Text;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;
using AdyenOutLoud.Services;
using AdyenOutLoud.TestSupport;

namespace AdyenOutLoud.E2ETests;

/// <summary>
/// The app's real Core logic (configuration, relay connection loop, envelope parsing, de-duplication) wired to the real local Worker. Only the operating-system boundary is replaced:
/// announcement playback is recorded instead of played, and settings/config storage is in memory.
/// </summary>
internal sealed class ListeningApp : IAsyncDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private readonly RelayConnectionService _relay;
    private readonly RecordingPlayer _player;
    private readonly object _sync = new();
    private readonly List<RelayStatus> _statuses = [];
    private readonly List<AnnouncementResult> _announcements = [];
    private readonly List<string> _diagnostics = [];

    private ListeningApp(RelayConnectionService relay, RecordingPlayer player)
    {
        _relay = relay;
        _player = player;
        _relay.StatusChanged += (_, status) => { lock (_sync) { _statuses.Add(status); } };
        _relay.AnnouncementCompleted += (_, result) => { lock (_sync) { _announcements.Add(result); } };
        _relay.Diagnostic += (_, message) => { lock (_sync) { _diagnostics.Add(message); } };
    }

    public IReadOnlyList<(AnnouncementSound Sound, AppLanguage Language)> Played => _player.Snapshot();

    public IReadOnlyList<AnnouncementResult> Announcements
    {
        get { lock (_sync) { return [.. _announcements]; } }
    }

    /// <summary>Pairs the way a merchant would (two approved payments, then both receipt codes) and starts listening.</summary>
    public static async Task<ListeningApp> StartAsync(LocalWorker worker, string terminalSerial, AppLanguage? language = null)
    {
        var app = Create(worker, terminalSerial, language, out var configuration);
        string[] receipts = [Webhooks.NextPsp(), Webhooks.NextPsp()];
        foreach (var psp in receipts) await worker.PostWebhookAsync(Webhooks.Approved(psp, terminalSerial));
        await configuration.PairAsync(terminalSerial, [.. receipts.Select(psp => psp[^4..])]);

        app._relay.Start();
        await app.WaitForStateAsync(RelayConnectionState.Listening);
        return app;
    }

    /// <summary>Starts listening with a stored pairing the relay never issued.</summary>
    public static async Task<ListeningApp> StartUnpairedAsync(LocalWorker worker, string terminalSerial, string forgedToken)
    {
        var app = Create(worker, terminalSerial, null, out _, new InMemoryConfigurationStore(new(terminalSerial, forgedToken)));
        app._relay.Start();
        await app.WaitForStateAsync(RelayConnectionState.NeedsAttention);
        return app;
    }

    public IReadOnlyList<RelayStatus> Statuses
    {
        get { lock (_sync) { return [.. _statuses]; } }
    }

    /// <summary>An HTTP client for the relay's pairing endpoint that maps the app's https URL onto the local http Worker.</summary>
    public static HttpClient RelayHttpClient() => new(new PlainHttpHandler());

    private static ListeningApp Create(LocalWorker worker, string terminalSerial, AppLanguage? language, out RelayConfigurationService configuration, InMemoryConfigurationStore? store = null)
    {
        var settings = new InMemorySettings { SelectedLanguage = language ?? AppLanguage.English };
        // Production requires an https relay URL; the local Worker speaks plain http, so the connection
        // factory maps wss -> ws and the HTTP handler maps https -> http. The URLs, paths, pairing and
        // terminal-serial handling are the real ones.
        configuration = new RelayConfigurationService(
            store ?? new InMemoryConfigurationStore(),
            new Uri($"https://{worker.BaseUri.Authority}"),
            RelayHttpClient());

        var player = new RecordingPlayer();
        return new ListeningApp(
            new RelayConnectionService(configuration, new LocalConnectionFactory(), settings, player),
            player);
    }

    public async Task StopAsync() => await _relay.StopAsync();

    public async Task ResumeAsync()
    {
        _relay.Start();
        await WaitForStateAsync(RelayConnectionState.Listening, afterStatusCount: StatusCount);
    }

    public Task WaitForStateAsync(RelayConnectionState state) => WaitForStateAsync(state, afterStatusCount: 0);

    public async Task WaitForPlayedAsync(int count, TimeSpan? timeout = null)
    {
        await PollAsync(() => _player.Snapshot().Count >= count, timeout ?? DefaultTimeout,
            () => $"Expected {count} announcement(s) played but heard {_player.Snapshot().Count}. {History}");
    }

    public async Task WaitForAnnouncementsAsync(int count, TimeSpan? timeout = null)
    {
        await PollAsync(() => Announcements.Count >= count, timeout ?? DefaultTimeout,
            () => $"Expected {count} completed announcement(s) but saw {Announcements.Count}. {History}");
    }

    public async ValueTask DisposeAsync() => await _relay.DisposeAsync();

    private string History
    {
        get { lock (_sync) { return $"Statuses: {string.Join(" | ", _statuses.Select(s => $"{s.State}: {s.Detail}"))}. Diagnostics: {string.Join(" | ", _diagnostics)}"; } }
    }

    private int StatusCount
    {
        get { lock (_sync) { return _statuses.Count; } }
    }

    private async Task WaitForStateAsync(RelayConnectionState state, int afterStatusCount)
    {
        await PollAsync(
            () => { lock (_sync) { return _statuses.Skip(afterStatusCount).Any(status => status.State == state); } },
            DefaultTimeout,
            () => { lock (_sync) { return $"Never reached {state}. Statuses: {string.Join(" | ", _statuses.Select(s => $"{s.State}: {s.Detail}"))}"; } });
    }

    private static async Task PollAsync(Func<bool> condition, TimeSpan timeout, Func<string> failure)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(failure());
            await Task.Delay(25);
        }
    }

    private sealed class RecordingPlayer : IAnnouncementPlayer
    {
        private readonly List<(AnnouncementSound Sound, AppLanguage Language)> _played = [];

        public Task PlayAsync(AnnouncementSound sound, AppLanguage language, CancellationToken cancellationToken)
        {
            lock (_played) { _played.Add((sound, language)); }
            return Task.CompletedTask;
        }

        public List<(AnnouncementSound Sound, AppLanguage Language)> Snapshot()
        {
            lock (_played) { return [.. _played]; }
        }
    }

    private sealed class InMemorySettings : ISettingsService
    {
        private readonly HashSet<string> _seen = [];

        public AppLanguage SelectedLanguage { get; set; } = AppLanguage.English;

        public Task<bool> TryReserveEventIdAsync(string eventId, DateTimeOffset receivedAt, CancellationToken cancellationToken)
        {
            lock (_seen) { return Task.FromResult(_seen.Add(eventId)); }
        }
    }

    private sealed class InMemoryConfigurationStore(RelayPairing? value = null) : IRelayConfigurationStore
    {
        private RelayPairing? _value = value;

        public Task<RelayPairing?> GetAsync() => Task.FromResult(_value);

        public Task SetAsync(RelayPairing pairing)
        {
            _value = pairing;
            return Task.CompletedTask;
        }
    }

    private sealed class PlainHttpHandler : DelegatingHandler
    {
        public PlainHttpHandler() : base(new HttpClientHandler()) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri = new UriBuilder(request.RequestUri!) { Scheme = "http", Port = request.RequestUri!.Port }.Uri;
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class LocalConnectionFactory : IRelayConnectionFactory
    {
        public IRelayConnection Create() => new LocalConnection();
    }

    // Same framing rules as the app's ClientWebSocketConnection (text frames only, 64 KiB cap), minus TLS.
    private sealed class LocalConnection : IRelayConnection
    {
        private const int MaxMessageBytes = 64 * 1024;
        private readonly ClientWebSocket _socket = new();

        public async Task ConnectAsync(Uri uri, string accessToken, CancellationToken cancellationToken)
        {
            _socket.Options.CollectHttpResponseDetails = true;
            _socket.Options.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            try { await _socket.ConnectAsync(new UriBuilder(uri) { Scheme = "ws" }.Uri, cancellationToken); }
            catch (WebSocketException ex) when (_socket.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new RelayUnauthorizedException("The relay did not accept this device's pairing.", ex);
            }
        }

        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            using var message = new MemoryStream();
            while (true)
            {
                var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                if (result.MessageType != WebSocketMessageType.Text) throw new WebSocketException("The relay sent a non-text message.");
                if (message.Length + result.Count > MaxMessageBytes) throw new WebSocketException("The relay message exceeded 64 KiB.");
                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) return Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
            }
        }

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "E2E close", cancellationToken);
            }
        }

        public ValueTask DisposeAsync()
        {
            _socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
