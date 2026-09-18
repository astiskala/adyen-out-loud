using System.Globalization;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;
using AdyenOutLoud.Services;

namespace AdyenOutLoud.Tests;

public sealed class AnnouncementContractTests
{
    [Fact]
    public async Task NewEventIsPersistedBeforeSpeechAndThenAcknowledged()
    {
        var calls = new List<string>();
        var service = Create(new Settings(calls), new Speech(calls), new Localization(calls));
        var result = await service.AnnounceAsync(Message(), CancellationToken.None);

        Assert.Equal(["persist", "localize", "speak"], calls);
        Assert.True(result.ShouldAcknowledge);
        Assert.True(result.WasSpoken);
    }

    [Fact]
    public async Task DuplicateEventIsAcknowledgedWithoutSpeech()
    {
        var calls = new List<string>();
        var service = Create(new Settings(calls, isNew: false), new Speech(calls), new Localization(calls));
        var result = await service.AnnounceAsync(Message(), CancellationToken.None);

        Assert.Equal(["persist"], calls);
        Assert.True(result.ShouldAcknowledge);
        Assert.True(result.WasDuplicate);
        Assert.False(result.WasSpoken);
    }

    [Fact]
    public async Task TextToSpeechFailureStillAcknowledgesPoisonMessage()
    {
        var calls = new List<string>();
        var service = Create(new Settings(calls), new FailingSpeech(calls), new Localization(calls));
        var result = await service.AnnounceAsync(Message(), CancellationToken.None);

        Assert.Equal(["persist", "localize", "speak"], calls);
        Assert.True(result.ShouldAcknowledge);
        Assert.False(result.WasSpoken);
        Assert.Contains("voice failed", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NullAmountIsPersistedAndAcknowledgedWithoutSpeech()
    {
        var calls = new List<string>();
        var service = Create(new Settings(calls), new Speech(calls), new Localization(calls));
        var result = await service.AnnounceAsync(Message() with { Amount = null }, CancellationToken.None);

        Assert.Equal(["persist"], calls);
        Assert.True(result.ShouldAcknowledge);
        Assert.False(result.WasSpoken);
    }

    [Fact]
    public void FourLanguagesHaveIndependentResxAnnouncementsAndLocalizedFallbackMethod()
    {
        var localization = new ResxLocalizationService();
        Assert.Collection(AppLanguage.All,
            language => Assert.StartsWith("Payment of", localization.CreatePaymentAnnouncement(Message() with { PaymentMethod = null }, language)),
            language => Assert.StartsWith("已通过", localization.CreatePaymentAnnouncement(Message() with { PaymentMethod = null }, language)),
            language => Assert.StartsWith("Bayaran sebanyak", localization.CreatePaymentAnnouncement(Message() with { PaymentMethod = null }, language)),
            language => Assert.EndsWith("பெறப்பட்டது.", localization.CreatePaymentAnnouncement(Message() with { PaymentMethod = null }, language)));
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
    public void CreatingAnAnnouncementForAMessageWithNoAmountThrows()
    {
        var localization = new ResxLocalizationService();
        Assert.Throws<ArgumentException>(() => localization.CreatePaymentAnnouncement(Message() with { Amount = null }, AppLanguage.English));
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

    private static PaymentAnnouncementService Create(ISettingsService settings, ITextToSpeechService speech, ILocalizationService localization) =>
        new(settings, speech, localization, new Clock());

    internal static PaymentMessage Message() => new(
        "event-1", "payment_succeeded", DateTimeOffset.Parse("2026-09-18T12:00:00Z", CultureInfo.InvariantCulture),
        "P400Plus-123", "txn-1", "PSP-1", "visa", new("SGD", 1050));

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

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch; }
}
