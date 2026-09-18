using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;
using AdyenOutLoud.Services;

namespace AdyenOutLoud.Tests;

public sealed class RelayConnectionContractTests
{
    private const string Envelope = """
        {"protocol":1,"message":{"id":"event-1","type":"payment_succeeded","occurredAt":"2026-09-18T12:00:00Z","terminalId":"P400Plus-123","transactionId":"txn-1","pspReference":"PSP-1","paymentMethod":"visa","amount":{"currency":"SGD","valueMinor":1050}}}
        """;

    [Fact]
    public async Task ConnectionUsesDerivedWebSocketAndSendsNoHelloBeforeAck()
    {
        var connection = new Connection(Envelope);
        var service = new RelayConnectionService(new Identity(), new Factory(connection), new Announcement(), new Delay());

        service.Start();
        await connection.AckSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync();

        Assert.Equal("wss://relay.example.com/v1/i/token/ws", connection.ConnectedUri!.AbsoluteUri);
        Assert.Equal(["{\"type\":\"ack\",\"id\":\"event-1\"}"], connection.Sent);
    }

    [Fact]
    public async Task IoFailureReconnectsWithBoundedBackoff()
    {
        var failed = new Connection(new IOException("offline"));
        var recovered = new Connection(Envelope);
        var factory = new Factory(failed, recovered);
        var delay = new Delay();
        var service = new RelayConnectionService(new Identity(), factory, new Announcement(), delay);

        service.Start();
        await recovered.AckSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync();

        Assert.Equal(2, factory.Created);
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(delay.Delays));
    }

    [Fact]
    public async Task StopBeforeStartIsANoOp()
    {
        var service = new RelayConnectionService(new Identity(), new Factory(), new Announcement(), new Delay());
        await service.StopAsync();
    }

    [Fact]
    public async Task StartingTwiceWhileRunningCreatesOnlyOneConnection()
    {
        var connection = new Connection(Envelope);
        var factory = new Factory(connection);
        var service = new RelayConnectionService(new Identity(), factory, new Announcement(), new Delay());

        service.Start();
        service.Start();
        await connection.AckSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync();

        Assert.Equal(1, factory.Created);
    }

    [Fact]
    public async Task IdentityFailureReportsNeedsAttentionAndStopsWithoutConnecting()
    {
        var statuses = new List<RelayConnectionState>();
        var factory = new Factory();
        var service = new RelayConnectionService(new FailingIdentity(), factory, new Announcement(), new Delay());
        service.StatusChanged += (_, status) => statuses.Add(status.State);

        service.Start();
        await Task.Delay(50);
        await service.StopAsync();

        Assert.Equal(0, factory.Created);
        Assert.Contains(RelayConnectionState.NeedsAttention, statuses);
    }

    [Fact]
    public async Task InvalidRelayEnvelopeIsIgnoredWithoutAcknowledgment()
    {
        var connection = new Connection("not a valid envelope");
        var diagnostics = new List<string>();
        var service = new RelayConnectionService(new Identity(), new Factory(connection), new Announcement(), new Delay());
        service.Diagnostic += (_, message) => diagnostics.Add(message);

        service.Start();
        await Task.Delay(50);
        await service.StopAsync();

        Assert.Empty(connection.Sent);
        Assert.Contains(diagnostics, message => message.Contains("invalid relay envelope", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RelayClosingTheConnectionTriggersAReconnectWithBackoff()
    {
        var closed = new Connection(ClosedByRelay);
        var recovered = new Connection(Envelope);
        var factory = new Factory(closed, recovered);
        var delay = new Delay();
        var service = new RelayConnectionService(new Identity(), factory, new Announcement(), delay);

        service.Start();
        await recovered.AckSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync();

        Assert.Equal(2, factory.Created);
        Assert.Single(delay.Delays);
    }

    [Fact]
    public async Task AnUnexpectedExceptionStopsTheLoopAndReportsNeedsAttention()
    {
        var statuses = new List<RelayConnectionState>();
        var connection = new Connection(new InvalidOperationException("boom"));
        var factory = new Factory(connection);
        var service = new RelayConnectionService(new Identity(), factory, new Announcement(), new Delay());
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
        var service = new RelayConnectionService(new Identity(), new Factory(connection), new Announcement(), new Delay());

        service.Start();
        await connection.AckSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.DisposeAsync();
    }

    private sealed class Identity : IInstanceIdentityService
    {
        public Task<InstanceIdentity> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(
            new InstanceIdentity("token", new("https://relay.example.com/v1/i/token"), new("wss://relay.example.com/v1/i/token/ws")));
    }

    private sealed class FailingIdentity : IInstanceIdentityService
    {
        public Task<InstanceIdentity> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<InstanceIdentity>(new InvalidOperationException("token store unavailable"));
    }

    private sealed class Announcement : IPaymentAnnouncementService
    {
        public event EventHandler<AnnouncementResult>? AnnouncementCompleted { add { } remove { } }
        public Task<AnnouncementResult> AnnounceAsync(PaymentMessage message, CancellationToken cancellationToken) => Task.FromResult(
            new AnnouncementResult(message.Id, true, false, true, "ok", message, null));
    }

    /// <summary>Placed in <see cref="Connection"/>'s receive queue to simulate the relay closing the socket.</summary>
    private static readonly object ClosedByRelay = new();

    private sealed class Connection(params object[] receives) : IRelayConnection
    {
        private readonly Queue<object> _receives = new(receives);
        public Uri? ConnectedUri { get; private set; }
        public List<string> Sent { get; } = [];
        public TaskCompletionSource AckSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) { ConnectedUri = uri; return Task.CompletedTask; }
        public Task SendAsync(string message, CancellationToken cancellationToken) { Sent.Add(message); AckSent.TrySetResult(); return Task.CompletedTask; }
        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            if (_receives.TryDequeue(out var value))
            {
                if (ReferenceEquals(value, ClosedByRelay)) return null;
                if (value is Exception exception) throw exception;
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
        public IRelayConnection Create() { Created++; return _connections.Dequeue(); }
    }

    private sealed class Delay : IRetryDelay
    {
        public List<TimeSpan> Delays { get; } = [];
        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) { Delays.Add(delay); return Task.CompletedTask; }
    }
}
