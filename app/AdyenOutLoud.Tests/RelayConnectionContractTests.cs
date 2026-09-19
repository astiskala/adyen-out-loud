using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;
using AdyenOutLoud.Services;

namespace AdyenOutLoud.Tests;

public sealed class RelayConnectionContractTests
{
    private const string Envelope = """
        {"protocol":2,"message":{"id":"event-1","type":"payment_succeeded","occurredAt":"2026-09-18T12:00:00Z","terminalId":"P400Plus-123","transactionId":"txn-1","pspReference":"PSP-1"}}
        """;

    [Fact]
    public async Task ConnectionUsesDerivedWebSocketAndReceivesThePayment()
    {
        var connection = new Connection(Envelope);
        var service = new RelayConnectionService(new Configuration(), new Factory(connection), new Announcement(), new Player());

        service.Start();
        await connection.MessageReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync();

        Assert.Equal("wss://relay.example.com/ws/324688170", connection.ConnectedUri!.AbsoluteUri);
        Assert.Equal("device-token", connection.ConnectedToken);
    }

    [Fact]
    public async Task ARefusedTokenAsksForPairingAgainInsteadOfRetrying()
    {
        var statuses = new List<RelayStatus>();
        var factory = new Factory(new Connection(), new Connection(Envelope));
        var service = new RelayConnectionService(new Configuration(), factory, new Announcement(), new Player());
        service.StatusChanged += (_, status) => statuses.Add(status);
        factory.FailConnectWith = new RelayUnauthorizedException();

        service.Start();
        await Task.Delay(50);
        await service.StopAsync();

        Assert.Equal(1, factory.Created);
        Assert.Contains(statuses, s => s.State == RelayConnectionState.NeedsAttention && s.Detail.Contains("Pair it", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IoFailureReconnectsWithBoundedBackoff()
    {
        var failed = new Connection(new IOException("offline"));
        var recovered = new Connection(Envelope);
        var factory = new Factory(failed, recovered);
        var service = new RelayConnectionService(new Configuration(), factory, new Announcement(), new Player());

        service.Start();
        await recovered.MessageReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync();

        Assert.Equal(2, factory.Created);
    }

    [Fact]
    public async Task StopBeforeStartIsANoOp()
    {
        var service = new RelayConnectionService(new Configuration(), new Factory(), new Announcement(), new Player());
        await service.StopAsync();
    }

    [Fact]
    public async Task StartingTwiceWhileRunningCreatesOnlyOneConnection()
    {
        var connection = new Connection(Envelope);
        var factory = new Factory(connection);
        var service = new RelayConnectionService(new Configuration(), factory, new Announcement(), new Player());

        service.Start();
        service.Start();
        await connection.MessageReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync();

        Assert.Equal(1, factory.Created);
    }

    [Fact]
    public async Task ConfigurationFailureReportsNeedsAttentionAndStopsWithoutConnecting()
    {
        var statuses = new List<RelayConnectionState>();
        var factory = new Factory();
        var service = new RelayConnectionService(new FailingConfiguration(), factory, new Announcement(), new Player());
        service.StatusChanged += (_, status) => statuses.Add(status.State);

        service.Start();
        await Task.Delay(50);
        await service.StopAsync();

        Assert.Equal(0, factory.Created);
        Assert.Contains(RelayConnectionState.NeedsAttention, statuses);
    }

    [Fact]
    public async Task AnUnpairedDeviceIsAskedToPairAndDoesNotConnect()
    {
        var statuses = new List<RelayStatus>();
        var factory = new Factory();
        var service = new RelayConnectionService(new UnconfiguredConfiguration(), factory, new Announcement(), new Player());
        service.StatusChanged += (_, status) => statuses.Add(status);

        service.Start();
        await Task.Delay(50);
        await service.StopAsync();

        Assert.Equal(0, factory.Created);
        Assert.Contains(statuses, s => s.State == RelayConnectionState.NeedsAttention && s.Detail.Contains("terminal serial number", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidRelayEnvelopeIsIgnored()
    {
        var connection = new Connection("not a valid envelope");
        var diagnostics = new List<string>();
        var service = new RelayConnectionService(new Configuration(), new Factory(connection), new Announcement(), new Player());
        service.Diagnostic += (_, message) => diagnostics.Add(message);

        service.Start();
        await Task.Delay(50);
        await service.StopAsync();

        Assert.Contains(diagnostics, message => message.Contains("invalid relay envelope", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RelayClosingTheConnectionTriggersAReconnectWithBackoff()
    {
        var closed = new Connection(ClosedByRelay);
        var recovered = new Connection(Envelope);
        var factory = new Factory(closed, recovered);
        var service = new RelayConnectionService(new Configuration(), factory, new Announcement(), new Player());

        service.Start();
        await recovered.MessageReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync();

        Assert.Equal(2, factory.Created);
    }

    [Fact]
    public async Task AnUnexpectedExceptionStopsTheLoopAndReportsNeedsAttention()
    {
        var statuses = new List<RelayConnectionState>();
        var connection = new Connection(new InvalidOperationException("boom"));
        var factory = new Factory(connection);
        var service = new RelayConnectionService(new Configuration(), factory, new Announcement(), new Player());
        service.StatusChanged += (_, status) => statuses.Add(status.State);

        service.Start();
        await Task.Delay(50);
        await service.StopAsync();

        Assert.Equal(1, factory.Created);
        Assert.Contains(RelayConnectionState.NeedsAttention, statuses);
    }

    [Fact]
    public async Task DisposeAsyncStopsTheRunningLoop()
    {
        var connection = new Connection(Envelope);
        var service = new RelayConnectionService(new Configuration(), new Factory(connection), new Announcement(), new Player());

        service.Start();
        await connection.MessageReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.DisposeAsync();
    }

    private sealed class Configuration : IRelayConfigurationService
    {
        public Task<RelayConfiguration?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<RelayConfiguration?>(
            new("324688170", new("wss://relay.example.com/ws/324688170"), "device-token"));
        public Task<RelayConfiguration> PairAsync(string terminalSerial, IReadOnlyList<string> receiptCodes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by these tests.");
    }

    private sealed class UnconfiguredConfiguration : IRelayConfigurationService
    {
        public Task<RelayConfiguration?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<RelayConfiguration?>(null);
        public Task<RelayConfiguration> PairAsync(string terminalSerial, IReadOnlyList<string> receiptCodes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by these tests.");
    }

    private sealed class FailingConfiguration : IRelayConfigurationService
    {
        public Task<RelayConfiguration?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<RelayConfiguration?>(new InvalidOperationException("configuration store unavailable"));
        public Task<RelayConfiguration> PairAsync(string terminalSerial, IReadOnlyList<string> receiptCodes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by these tests.");
    }

    private sealed class Announcement : ISettingsService
    {
        public AppLanguage SelectedLanguage { get; set; } = AppLanguage.English;
        public Task<bool> TryReserveEventIdAsync(string eventId, DateTimeOffset receivedAt, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class Player : IAnnouncementPlayer
    {
        public Task PlayAsync(AnnouncementSound sound, AppLanguage language, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Placed in <see cref="Connection"/>'s receive queue to simulate the relay closing the socket.</summary>
    private static readonly object ClosedByRelay = new();

    private sealed class Connection(params object[] receives) : IRelayConnection
    {
        private readonly Queue<object> _receives = new(receives);
        public Uri? ConnectedUri { get; private set; }
        public string? ConnectedToken { get; private set; }
        public TaskCompletionSource MessageReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? FailConnectWith { get; set; }
        public Task ConnectAsync(Uri uri, string accessToken, CancellationToken cancellationToken)
        {
            if (FailConnectWith is not null) return Task.FromException(FailConnectWith);
            ConnectedUri = uri;
            ConnectedToken = accessToken;
            return Task.CompletedTask;
        }
        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            if (_receives.TryDequeue(out var value))
            {
                if (ReferenceEquals(value, ClosedByRelay)) return null;
                if (value is Exception exception) throw exception;
                MessageReceived.TrySetResult();
                return (string)value;
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
        public Task CloseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Factory(params Connection[] connections) : IRelayConnectionFactory
    {
        private readonly Queue<Connection> _connections = new(connections);
        public int Created { get; private set; }
        public Exception? FailConnectWith { get; set; }
        public IRelayConnection Create()
        {
            Created++;
            var connection = _connections.Dequeue();
            connection.FailConnectWith = FailConnectWith;
            return connection;
        }
    }
}
