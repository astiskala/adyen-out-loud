using System.Net;
using AdyenOutLoud.TestSupport;

namespace AdyenOutLoud.UITests;

/// <summary>
/// The whole product through the real app: an Adyen Display webhook is POSTed to a local Worker, and the running iOS app
/// — connected over genuine TLS WebSockets — shows and announces the payment.
/// </summary>
public sealed class PaymentUiTests(UiEnvironment environment) : AppTest(environment)
{
    private const string Terminal = "324688170";

    [Fact]
    public async Task AConfiguredAppListensAndShowsAPaymentWhenOneIsTaken()
    {
        await ListenAsync();
        var psp = Webhooks.NextPsp();

        using var response = await Environment.PostWebhookAsync(Webhooks.Approved(psp, Terminal));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await WaitForPaymentAsync(psp, "the payment to appear under Latest event");
        var latest = await App.LabelAsync(LatestEvent);
        Assert.Contains($"Terminal V400m-{Terminal}", latest, StringComparison.Ordinal);
        Assert.Contains($"Event payment:{psp}", latest, StringComparison.Ordinal);
        Assert.NotEqual(DiagnosticPlaceholder, await App.LabelAsync(Diagnostic));
    }

    [Fact]
    public async Task ARetriedWebhookDoesNotReplaceTheLatestEventWithAnotherPayment()
    {
        await ListenAsync();
        var first = Webhooks.NextPsp();
        var second = Webhooks.NextPsp();

        await Environment.PostWebhookAsync(Webhooks.Approved(first, Terminal));
        await WaitForPaymentAsync(first, "the first payment");
        await Environment.PostWebhookAsync(Webhooks.Approved(first, Terminal));
        await Environment.PostWebhookAsync(Webhooks.Approved(second, Terminal));

        await WaitForPaymentAsync(second, "the second, distinct payment");
    }

    [Fact]
    public async Task ADeclinedPaymentIsNotShown()
    {
        await ListenAsync();
        var sentinel = Webhooks.NextPsp();

        await Environment.PostWebhookAsync(Webhooks.Declined(Terminal));
        await Environment.PostWebhookAsync(Webhooks.Approved(sentinel, Terminal));

        await WaitForPaymentAsync(sentinel, "the approved payment");
        Assert.DoesNotContain("DECLINEDPSP", await App.LabelAsync(LatestEvent), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PaymentsForAnotherTerminalAreNotShown()
    {
        await ListenAsync();
        var other = Webhooks.NextPsp();
        var mine = Webhooks.NextPsp();

        await Environment.PostWebhookAsync(Webhooks.Approved(other, "999999999"));
        await Environment.PostWebhookAsync(Webhooks.Approved(mine, Terminal));

        await WaitForPaymentAsync(mine, "this terminal's payment");
    }

    [Fact]
    public async Task TheAppReconnectsAfterBeingSentToTheBackground()
    {
        await ListenAsync();

        await App.ExecuteMobileAsync("backgroundApp", new { seconds = 3 });

        await App.WaitForLabelAsync(StatusTitle, title => title == "LISTENING", "the app to listen again after returning to the foreground", TimeSpan.FromSeconds(40));
        var psp = Webhooks.NextPsp();
        await Environment.PostWebhookAsync(Webhooks.Approved(psp, Terminal));
        await WaitForPaymentAsync(psp, "a payment after returning to the foreground");
    }

    private async Task ListenAsync()
    {
        await SaveConfigurationAsync(Terminal);
        await App.WaitForLabelAsync(StatusTitle, title => title == "LISTENING", "the app to connect to the relay", TimeSpan.FromSeconds(40));
    }
}
