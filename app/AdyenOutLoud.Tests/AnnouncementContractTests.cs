using System.Globalization;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;
using AdyenOutLoud.Services;

namespace AdyenOutLoud.Tests;

public sealed class AnnouncementContractTests
{
    [Fact]
    public async Task NewEventIsPersistedThenLocalizedThenSpoken()
    {
        var calls = new List<string>();
        var service = Create(new Settings(calls), new Speech(calls), new Localization(calls));
        var result = await service.AnnounceAsync(Message(), CancellationToken.None);

        Assert.Equal(["persist", "localize", "speak"], calls);
        Assert.True(result.WasSpoken);
    }

    [Fact]
    public async Task DuplicateEventIsNotSpokenAgain()
    {
        var calls = new List<string>();
        var service = Create(new Settings(calls, isNew: false), new Speech(calls), new Localization(calls));
        var result = await service.AnnounceAsync(Message(), CancellationToken.None);

        Assert.Equal(["persist"], calls);
        Assert.True(result.WasDuplicate);
        Assert.False(result.WasSpoken);
    }

    [Fact]
    public async Task TextToSpeechFailureIsRecordedWithoutThrowing()
    {
        var calls = new List<string>();
        var service = Create(new Settings(calls), new FailingSpeech(calls), new Localization(calls));
        var result = await service.AnnounceAsync(Message(), CancellationToken.None);

        Assert.Equal(["persist", "localize", "speak"], calls);
        Assert.False(result.WasSpoken);
        Assert.Contains("voice failed", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FourLanguagesHaveIndependentResxAnnouncements()
    {
        var localization = new ResxLocalizationService();
        Assert.Collection(AppLanguage.All,
            language => Assert.Equal("Payment successful.", localization.CreatePaymentAnnouncement(Message(), language)),
            language => Assert.Equal("付款成功。", localization.CreatePaymentAnnouncement(Message(), language)),
            language => Assert.Equal("Bayaran berjaya.", localization.CreatePaymentAnnouncement(Message(), language)),
            language => Assert.Equal("பணம் செலுத்துதல் வெற்றிகரமாக முடிந்தது.", localization.CreatePaymentAnnouncement(Message(), language)));
    }

    [Fact]
    public void EveryLanguageHasAnIndependentTestAnnouncement()
    {
        var localization = new ResxLocalizationService();
        foreach (var language in AppLanguage.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(localization.CreateTestAnnouncement(language)));
        }
    }

    [Fact]
    public async Task AnObserverExceptionDuringAnnouncementCompletedDoesNotPropagate()
    {
        var calls = new List<string>();
        var service = Create(new Settings(calls), new Speech(calls), new Localization(calls));
        service.AnnouncementCompleted += (_, _) => throw new InvalidOperationException("observer failed");

        var result = await service.AnnounceAsync(Message(), CancellationToken.None);

        Assert.True(result.WasSpoken);
    }

    private static RelayConnectionService Create(ISettingsService settings, ITextToSpeechService speech, ILocalizationService localization)
    {
        var configService = new FakeConfigService();
        var factory = new FakeConnectionFactory();
        return new RelayConnectionService(configService, factory, settings, speech, localization);
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

    private sealed class Speech(List<string> calls) : ITextToSpeechService
    {
        public Task<SpeechDiagnostic> SpeakAsync(string text, AppLanguage language, CancellationToken cancellationToken)
        {
            calls.Add("speak");
            return Task.FromResult(new SpeechDiagnostic(language.Locale, language.Locale, "voice ok"));
        }
    }

    private sealed class FailingSpeech(List<string> calls) : ITextToSpeechService
    {
        public Task<SpeechDiagnostic> SpeakAsync(string text, AppLanguage language, CancellationToken cancellationToken)
        {
            calls.Add("speak");
            throw new InvalidOperationException("unavailable");
        }
    }

    private sealed class Localization(List<string> calls) : ILocalizationService
    {
        public string CreatePaymentAnnouncement(PaymentMessage message, AppLanguage language) { calls.Add("localize"); return "payment"; }
        public string CreateTestAnnouncement(AppLanguage language) => "test";
    }

    private sealed class FakeConfigService : IRelayConfigurationService
    {
        public Task<RelayConfiguration?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<RelayConfiguration?>(null);
        public Task<RelayConfiguration> SaveAsync(Uri baseUrl, string terminalSerial, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeConnectionFactory : IRelayConnectionFactory
    {
        public IRelayConnection Create() => throw new NotImplementedException();
    }
}
