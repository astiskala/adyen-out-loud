using AdyenOutLoud.TestSupport;

namespace AdyenOutLoud.UITests;

/// <summary>Each test starts from a freshly installed app (and empty keychain), so tests cannot leak state into each other.</summary>
[Collection(UiDefinition.Name)]
public abstract class AppTest(UiEnvironment environment) : IAsyncLifetime
{
    internal const string StatusTitle = "StatusTitle";
    internal const string StatusDetail = "StatusDetail";
    internal const string ConfigurationStatus = "ConfigurationStatus";
    internal const string LatestEvent = "LatestEvent";
    internal const string Diagnostic = "Diagnostic";
    internal const string SerialField = "Terminal serial number";
    internal const string FirstReceiptField = "First receipt code";
    internal const string SecondReceiptField = "Second receipt code";
    internal const string PairButton = "Pair this device with the terminal";
    internal const string LanguagePicker = "Announcement language";
    internal const string TestVoiceButton = "Play a test announcement in the selected language";
    internal const string NoEventYet = "No payment event received yet.";
    internal const string DiagnosticPlaceholder = "Run Test voice to hear the selected announcement.";

    protected UiEnvironment Environment { get; } = environment;

    internal AppiumSession App => Environment.App;

    public async Task InitializeAsync() => await Environment.ResetAppAsync();

    public async Task DisposeAsync()
    {
        if (Environment.ArtifactsDirectory is { } directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                await File.WriteAllBytesAsync(Path.Combine(directory, $"{GetType().Name}-{Guid.NewGuid():N}.png"), await App.ScreenshotAsync());
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException or TimeoutException)
            {
                // A missing screenshot must never hide the test result.
            }
        }
    }

    /// <summary>
    /// Pairs the way a merchant would: two approved payments on the terminal, then its serial and the last 4 characters
    /// of each receipt's PSP reference, then Pair. The keyboard is dismissed first so it cannot cover the button.
    /// </summary>
    protected async Task PairAsync(string terminalSerial)
    {
        string[] receipts = [Webhooks.NextPsp(), Webhooks.NextPsp()];
        foreach (var psp in receipts) await Environment.PostWebhookAsync(Webhooks.Approved(psp, terminalSerial));
        await EnterPairingAsync(terminalSerial, receipts[0][^4..], receipts[1][^4..]);
    }

    /// <summary>Fills in the pairing form and taps Pair.</summary>
    protected async Task EnterPairingAsync(string terminalSerial, string firstCode, string secondCode)
    {
        await App.TypeAsync(SerialField, terminalSerial, pressReturn: true);
        await App.TypeAsync(FirstReceiptField, firstCode, pressReturn: true);
        await App.TypeAsync(SecondReceiptField, secondCode, pressReturn: true);
        await App.TapAsync(PairButton);
    }

    /// <summary>Waits for a payment to appear under Latest event; on failure the message includes the app's connection state.</summary>
    protected async Task WaitForPaymentAsync(string pspReference, string description = "the payment to appear under Latest event")
    {
        var started = DateTime.UtcNow;
        try
        {
            await App.WaitForLabelAsync(LatestEvent, text => text.Contains($"PSP {pspReference}", StringComparison.Ordinal), description, TimeSpan.FromSeconds(120));
            UiEnvironment.Log($"payment shown after {(DateTime.UtcNow - started).TotalSeconds:0.0}s");
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(
                $"{exception.Message} App state: {await App.LabelAsync(StatusTitle)} - {await App.LabelAsync(StatusDetail)} | diagnostic: {await App.LabelAsync(Diagnostic)} | worker log: {Environment.WorkerLogTail}",
                exception);
        }
    }

    protected async Task RelaunchAppAsync()
    {
        await App.ExecuteMobileAsync("terminateApp", new { bundleId = UiEnvironment.BundleId });
        await Environment.LaunchAppAsync();
    }
}
