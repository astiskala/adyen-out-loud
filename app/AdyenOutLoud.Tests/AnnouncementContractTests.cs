using System.Globalization;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;
using AdyenOutLoud.Services;

namespace AdyenOutLoud.Tests;

public sealed class AnnouncementContractTests
{
    [Fact]
    public async Task NewEventIsPersistedThenPlayed()
    {
        var calls = new List<string>();
        var service = Create(new Settings(calls), new Player(calls));
        var result = await service.AnnounceAsync(Message(), CancellationToken.None);

        Assert.Equal(["persist", "play"], calls);
        Assert.True(result.WasPlayed);
    }

    [Fact]
    public async Task DuplicateEventIsNotPlayedAgain()
    {
        var calls = new List<string>();
        var service = Create(new Settings(calls, isNew: false), new Player(calls));
        var result = await service.AnnounceAsync(Message(), CancellationToken.None);

        Assert.Equal(["persist"], calls);
        Assert.True(result.WasDuplicate);
        Assert.False(result.WasPlayed);
    }

    [Fact]
    public async Task PlaybackFailureIsRecordedWithoutThrowing()
    {
        var calls = new List<string>();
        var service = Create(new Settings(calls), new FailingPlayer(calls));
        var result = await service.AnnounceAsync(Message(), CancellationToken.None);

        Assert.Equal(["persist", "play"], calls);
        Assert.False(result.WasPlayed);
        Assert.Contains("playback failed", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheRecordingForTheSelectedLanguageIsPlayed()
    {
        var calls = new List<string>();
        var player = new Player(calls);
        var service = Create(new Settings(calls) { SelectedLanguage = AppLanguage.Tamil }, player);

        await service.AnnounceAsync(Message(), CancellationToken.None);

        Assert.Equal([(AnnouncementSound.PaymentReceived, AppLanguage.Tamil)], player.Played);
    }

    [Fact]
    public async Task AnObserverExceptionDuringAnnouncementCompletedDoesNotPropagate()
    {
        var calls = new List<string>();
        var service = Create(new Settings(calls), new Player(calls));
        service.AnnouncementCompleted += (_, _) => throw new InvalidOperationException("observer failed");

        var result = await service.AnnounceAsync(Message(), CancellationToken.None);

        Assert.True(result.WasPlayed);
    }

    private static RelayConnectionService Create(ISettingsService settings, IAnnouncementPlayer player)
    {
        var configService = new FakeConfigService();
        var factory = new FakeConnectionFactory();
        return new RelayConnectionService(configService, factory, settings, player);
    }

    internal static PaymentMessage Message() => new(
        "event-1", "payment_succeeded", DateTimeOffset.Parse("2026-09-18T12:00:00Z", CultureInfo.InvariantCulture),
        "P400Plus-123", "txn-1", "PSP-1");

    private sealed class Settings(List<string> calls, bool isNew = true) : ISettingsService
    {
        public AppLanguage SelectedLanguage { get; set; } = AppLanguage.English;
        public Task<bool> TryReserveEventIdAsync(string eventId, DateTimeOffset receivedAt, CancellationToken cancellationToken)
        {
            calls.Add("persist");
            return Task.FromResult(isNew);
        }
    }

    private sealed class Player(List<string> calls) : IAnnouncementPlayer
    {
        public List<(AnnouncementSound, AppLanguage)> Played { get; } = [];
        public Task PlayAsync(AnnouncementSound sound, AppLanguage language, CancellationToken cancellationToken)
        {
            calls.Add("play");
            Played.Add((sound, language));
            return Task.CompletedTask;
        }
    }

    private sealed class FailingPlayer(List<string> calls) : IAnnouncementPlayer
    {
        public Task PlayAsync(AnnouncementSound sound, AppLanguage language, CancellationToken cancellationToken)
        {
            calls.Add("play");
            throw new InvalidOperationException("unavailable");
        }
    }

    private sealed class FakeConfigService : IRelayConfigurationService
    {
        public Task<RelayConfiguration?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<RelayConfiguration?>(null);
        public Task<RelayConfiguration> SaveAsync(string terminalSerial, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeConnectionFactory : IRelayConnectionFactory
    {
        public IRelayConnection Create() => throw new NotImplementedException();
    }
}
