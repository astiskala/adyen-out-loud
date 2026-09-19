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

    private ListeningApp(RelayConnectionService relay, RecordingPlayer player)
    {
        _relay = relay;
        _player = player;
        _relay.StatusChanged += (_, status) => { lock (_sync) { _statuses.Add(status); } };
        _relay.AnnouncementCompleted += (_, result) => { lock (_sync) { _announcements.Add(result); } };
    }

    public IReadOnlyList<(AnnouncementSound Sound, AppLanguage Language)> Played => _player.Snapshot();

    public IReadOnlyList<AnnouncementResult> Announcements
    {
        get { lock (_sync) { return [.. _announcements]; } }
    }

    public static async Task<ListeningApp> StartAsync(LocalWorker worker, string terminalSerial, AppLanguage? language = null)
    {
        var settings = new InMemorySettings { SelectedLanguage = language ?? AppLanguage.English };
        // Production requires an https relay URL; the local Worker speaks plain http, so the connection
        // factory maps wss -> ws. The URL, path and terminal-serial handling are the real ones.
        var configuration = new RelayConfigurationService(new InMemoryConfigurationStore(), new Uri($"https://{worker.BaseUri.Authority}"));
        await configuration.SaveAsync(terminalSerial);

        var player = new RecordingPlayer();
        var app = new ListeningApp(
            new RelayConnectionService(configuration, new LocalConnectionFactory(), settings, player),
            player);
        app._relay.Start();
        await app.WaitForStateAsync(RelayConnectionState.Listening);
        return app;
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
            () => $"Expected {count} announcement(s) played but heard {_player.Snapshot().Count}.");
    }

    public async Task WaitForAnnouncementsAsync(int count, TimeSpan? timeout = null)
    {
        await PollAsync(() => Announcements.Count >= count, timeout ?? DefaultTimeout,
            () => $"Expected {count} completed announcement(s) but saw {Announcements.Count}.");
    }

    public async ValueTask DisposeAsync() => await _relay.DisposeAsync();

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

    private sealed class InMemoryConfigurationStore : IRelayConfigurationStore
    {
        private string? _value;

        public Task<string?> GetAsync() => Task.FromResult(_value);

        public Task SetAsync(string terminalSerial)
        {
            _value = terminalSerial;
            return Task.CompletedTask;
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

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
            _socket.ConnectAsync(new UriBuilder(uri) { Scheme = "ws" }.Uri, cancellationToken);

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
