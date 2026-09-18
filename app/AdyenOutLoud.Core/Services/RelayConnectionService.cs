using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

public sealed class RelayConnectionService(
    IInstanceIdentityService identityService,
    IRelayConnectionFactory connectionFactory,
    IPaymentAnnouncementService announcementService,
    IRetryDelay retryDelay) : IRelayConnectionService, IAsyncDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;

    public event EventHandler<RelayStatus>? StatusChanged;
    public event EventHandler<string>? Diagnostic;

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
        InstanceIdentity identity;
        try
        {
            identity = await identityService.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            SetStatus(RelayConnectionState.NeedsAttention, "Secure instance setup failed.");
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
                await connection.ConnectAsync(identity.WebSocketUrl, cancellationToken).ConfigureAwait(false);
                failures = 0;
                SetStatus(RelayConnectionState.Listening, "Connected and waiting for payments.");

                while (!cancellationToken.IsCancellationRequested)
                {
                    var json = await connection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                    if (json is null) throw new IOException("The relay closed the connection.");
                    if (!RelayProtocol.TryParsePayment(json, out var payment) || payment is null)
                    {
                        Diagnostic?.Invoke(this, "Ignored an invalid relay envelope.");
                        continue;
                    }

                    var result = await announcementService.AnnounceAsync(payment, cancellationToken).ConfigureAwait(false);
                    Diagnostic?.Invoke(this, result.Detail);
                    if (result.ShouldAcknowledge)
                    {
                        await connection.SendAsync(RelayProtocol.CreateAck(payment.Id), cancellationToken).ConfigureAwait(false);
                    }
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
                await retryDelay.WaitAsync(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                SetStatus(RelayConnectionState.NeedsAttention, exception.Message);
                Diagnostic?.Invoke(this, $"Relay stopped: {exception.Message}");
                break;
            }
        }
    }

    private static async Task TryCloseAsync(IRelayConnection connection)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try { await connection.CloseAsync(timeout.Token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or System.Net.WebSockets.WebSocketException) { }
    }

    private void SetStatus(RelayConnectionState state, string detail) => StatusChanged?.Invoke(this, new(state, detail));

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _runCancellation?.Dispose();
    }
}
