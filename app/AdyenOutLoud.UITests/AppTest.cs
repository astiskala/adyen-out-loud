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
    internal const string SaveButton = "Save the terminal serial number";
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

    /// <summary>Fills in the terminal serial and taps Save. The keyboard is dismissed first so it cannot cover the button.</summary>
    protected async Task SaveConfigurationAsync(string terminalSerial)
    {
        await App.TypeAsync(SerialField, terminalSerial, pressReturn: true);
        await App.TapAsync(SaveButton);
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
