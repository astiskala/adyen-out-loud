using System.Net;
using AdyenOutLoud.Models;
using AdyenOutLoud.TestSupport;

namespace AdyenOutLoud.E2ETests;

/// <summary>
/// End-to-end: a real Adyen Display webhook is POSTed to the real Worker running locally, travels through
/// its Durable Object over a real WebSocket, and is parsed, de-duplicated and "played" by the app's real
/// Core logic. Negative expectations are proven with a sentinel payment delivered afterwards — delivery is
/// ordered, so "sentinel heard, nothing else heard" needs no sleeping.
/// </summary>
[Collection(LocalWorkerDefinition.Name)]
public sealed class PaymentRelayE2ETests(LocalWorker worker)
{
    private const string Terminal = "324688170";

    public static TheoryData<string> LanguageCodes { get; } = [.. AppLanguage.All.Select(language => language.Code)];

    [Theory]
    [MemberData(nameof(LanguageCodes))]
    public async Task ApprovedPaymentIsAnnouncedInTheSelectedLanguage(string languageCode)
    {
        var language = AppLanguage.FromCode(languageCode);
        await using var app = await ListeningApp.StartAsync(worker, Terminal, language);

        using var response = await worker.PostWebhookAsync(Webhooks.Approved(Webhooks.NextPsp()));
        await app.WaitForPlayedAsync(1);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal((AnnouncementSound.PaymentReceived, language), Assert.Single(app.Played));
    }

    [Fact]
    public async Task OnlyTheTerminalThatTookThePaymentHearsIt()
    {
        await using var thisTerminal = await ListeningApp.StartAsync(worker, Terminal);
        await using var otherTerminal = await ListeningApp.StartAsync(worker, "111111111");

        await worker.PostWebhookAsync(Webhooks.Approved(Webhooks.NextPsp(), Terminal));
        await thisTerminal.WaitForPlayedAsync(1);
        await worker.PostWebhookAsync(Webhooks.Approved(Webhooks.NextPsp(), "111111111"));
        await otherTerminal.WaitForPlayedAsync(1);

        Assert.Single(thisTerminal.Played);
        Assert.Single(otherTerminal.Played);
    }

    [Fact]
    public async Task APaymentForATerminalNobodyIsListeningForIsDropped()
    {
        await using var app = await ListeningApp.StartAsync(worker, Terminal);

        using var unrelated = await worker.PostWebhookAsync(Webhooks.Approved(Webhooks.NextPsp(), "999999999"));
        await worker.PostWebhookAsync(Webhooks.Approved(Webhooks.NextPsp()));
        await app.WaitForPlayedAsync(1);

        Assert.Equal(HttpStatusCode.Accepted, unrelated.StatusCode);
        Assert.Single(app.Played);
    }

    [Fact]
    public async Task ARetriedWebhookIsAnnouncedOnlyOnce()
    {
        await using var app = await ListeningApp.StartAsync(worker, Terminal);
        var psp = Webhooks.NextPsp();

        await worker.PostWebhookAsync(Webhooks.Approved(psp));
        await worker.PostWebhookAsync(Webhooks.Approved(psp));
        await worker.PostWebhookAsync(Webhooks.Approved(Webhooks.NextPsp()));
        await app.WaitForAnnouncementsAsync(3);

        Assert.Equal(2, app.Played.Count);
        Assert.Equal([false, true, false], app.Announcements.Select(result => result.WasDuplicate));
    }

    [Fact]
    public async Task ADeclinedPaymentIsNeverAnnounced()
    {
        await using var app = await ListeningApp.StartAsync(worker, Terminal);

        using var declined = await worker.PostWebhookAsync(Webhooks.Declined());
        await worker.PostWebhookAsync(Webhooks.Approved(Webhooks.NextPsp()));
        await app.WaitForPlayedAsync(1);

        Assert.Equal(HttpStatusCode.Accepted, declined.StatusCode);
        Assert.Single(app.Played);
        Assert.Single(app.Announcements);
    }

    [Fact]
    public async Task PaymentsTakenBeforeTheAppConnectedAreNotReplayed()
    {
        await worker.PostWebhookAsync(Webhooks.Approved(Webhooks.NextPsp()));

        await using var app = await ListeningApp.StartAsync(worker, Terminal);
        await worker.PostWebhookAsync(Webhooks.Approved(Webhooks.NextPsp()));
        await app.WaitForPlayedAsync(1);

        Assert.Single(app.Played);
    }

    [Fact]
    public async Task ABackgroundedAppMissesPaymentsAndHearsTheNextOneAfterResuming()
    {
        await using var app = await ListeningApp.StartAsync(worker, Terminal);
        await worker.PostWebhookAsync(Webhooks.Approved(Webhooks.NextPsp()));
        await app.WaitForPlayedAsync(1);

        await app.StopAsync();
        await worker.PostWebhookAsync(Webhooks.Approved(Webhooks.NextPsp()));
        await app.ResumeAsync();
        await worker.PostWebhookAsync(Webhooks.Approved(Webhooks.NextPsp()));
        await app.WaitForPlayedAsync(2);

        Assert.Equal(2, app.Played.Count);
    }

    [Fact]
    public async Task ADeviceThatWasNeverPairedCannotListen()
    {
        await using var app = await ListeningApp.StartUnpairedAsync(worker, Terminal, "forged-token");

        Assert.Contains(app.Statuses, status => status.State == RelayConnectionState.NeedsAttention && status.Detail.Contains("Pair it", StringComparison.Ordinal));
        Assert.DoesNotContain(app.Statuses, status => status.State == RelayConnectionState.Listening);
    }

    [Fact]
    public async Task ReceiptsFromAnotherTerminalDoNotPairThisOne()
    {
        string[] receipts = [Webhooks.NextPsp(), Webhooks.NextPsp()];
        foreach (var psp in receipts) await worker.PostWebhookAsync(Webhooks.Approved(psp, "111111111"));
        var configuration = new AdyenOutLoud.Services.RelayConfigurationService(
            new NullStore(), new Uri($"https://{worker.BaseUri.Authority}"), ListeningApp.RelayHttpClient());

        var error = await Assert.ThrowsAsync<RelayPairingException>(() =>
            configuration.PairAsync(Terminal, [.. receipts.Select(psp => psp[^4..])]));

        Assert.Contains("don't match", error.Message, StringComparison.Ordinal);
    }

    private sealed class NullStore : AdyenOutLoud.Abstractions.IRelayConfigurationStore
    {
        public Task<RelayPairing?> GetAsync() => Task.FromResult<RelayPairing?>(null);
        public Task SetAsync(RelayPairing pairing) => Task.CompletedTask;
    }

    [Fact]
    public async Task TheWorkerRejectsBadRequestsWithoutAnnouncingAnything()
    {
        await using var app = await ListeningApp.StartAsync(worker, Terminal);

        using var notJson = await worker.PostWebhookAsync("{not json");
        using var wrongType = await worker.Http.PostAsync(worker.WebhookUrl, new StringContent("x", System.Text.Encoding.UTF8, "text/plain"));
        using var wrongMethod = await worker.Http.GetAsync(worker.WebhookUrl);
        await worker.PostWebhookAsync(Webhooks.Approved(Webhooks.NextPsp()));
        await app.WaitForPlayedAsync(1);

        Assert.Equal(HttpStatusCode.BadRequest, notJson.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
        Assert.Single(app.Played);
    }
}
