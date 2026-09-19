using AdyenOutLoud.TestSupport;

namespace AdyenOutLoud.UITests;

/// <summary>
/// Everything the UI tests share for the whole run: the app under test, the simulator, the Appium server, and a local
/// Worker reachable over trusted HTTPS so the real app can connect to it exactly as it would to production.
/// </summary>
public sealed class UiEnvironment : IAsyncLifetime
{
    public const string BundleId = "com.adyen.outloud";
    private readonly LocalWorker _worker = new();
    private readonly List<Func<ValueTask>> _cleanup = [];
    private Uri? _relayBase;
    private string? _authorityFile;

    public Uri AppiumServer { get; private set; } = null!;

    public string Udid { get; private set; } = null!;

    public string AppPath { get; private set; } = null!;

    public string? ArtifactsDirectory { get; private set; }

    /// <summary>One Appium session for the whole run: starting WebDriverAgent takes ~45s, resetting the app takes ~2s.</summary>
    internal AppiumSession App { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        AppiumServer = new Uri(Required("ADYEN_UITEST_APPIUM"));
        Udid = Required("ADYEN_UITEST_UDID");
        AppPath = Required("ADYEN_UITEST_APP");
        ArtifactsDirectory = Environment.GetEnvironmentVariable("ADYEN_UITEST_ARTIFACTS");
        if (!Directory.Exists(AppPath))
        {
            throw new InvalidOperationException($"The app bundle does not exist: {AppPath}");
        }

        await _worker.InitializeAsync();
        var authority = new TestCertificateAuthority();
        _cleanup.Add(() => { authority.Dispose(); return ValueTask.CompletedTask; });
        var proxy = new TlsProxy(authority.ServerCertificate, _worker.BaseUri);
        _cleanup.Add(proxy.DisposeAsync);
        _relayBase = proxy.BaseUri;
        _authorityFile = Path.Combine(Path.GetTempPath(), $"adyen-ui-ca-{Guid.NewGuid():N}.pem");
        await File.WriteAllTextAsync(_authorityFile, authority.AuthorityPem);

        await ResetSimulatorStateAsync();
        App = await AppiumSession.StartAsync(AppiumServer, Udid, BundleId);
        _cleanup.Add(App.DisposeAsync);
    }

    public async Task DisposeAsync()
    {
        for (var index = _cleanup.Count - 1; index >= 0; index--) await _cleanup[index]();
        await _worker.DisposeAsync();
        if (_authorityFile is not null) File.Delete(_authorityFile);
    }

    public static void Log(string message) => File.AppendAllText(Path.Combine(Path.GetTempPath(), "adyen-ui-timing.log"), message + "\n");

    /// <summary>The last lines of the local Worker's output — its request log — for failure diagnostics.</summary>
    public string WorkerLogTail => string.Join(" // ", _worker.Log.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(12));

    public Task<HttpResponseMessage> PostWebhookAsync(string json) => _worker.PostWebhookAsync(json);

    /// <summary>Puts the app back to "freshly installed and launched, with the local test CA trusted".</summary>
    public async Task ResetAppAsync()
    {
        await App.ExecuteMobileAsync("terminateApp", new { bundleId = BundleId });
        await ResetSimulatorStateAsync();
        await LaunchAppAsync();
    }

    /// <summary>
    /// Launches the app pointed at the local Worker (over trusted TLS) instead of the hosted relay; Debug builds honour this override.
    /// </summary>
    public Task LaunchAppAsync() => App.ExecuteMobileAsync(
        "launchApp",
        new { bundleId = BundleId, environment = new Dictionary<string, string> { ["ADYEN_OUT_LOUD_RELAY_URL"] = _relayBase!.AbsoluteUri } });

    private async Task ResetSimulatorStateAsync()
    {
        await TryAsync("terminate", BundleId);
        await TryAsync("uninstall", BundleId);
        await Simulator.SimctlAsync("keychain", Udid, "reset");
        await Simulator.SimctlAsync("keychain", Udid, "add-root-cert", _authorityFile!);
        await Simulator.SimctlAsync("install", Udid, AppPath);
    }

    private async Task TryAsync(string command, string bundleId)
    {
        try { await Simulator.SimctlAsync(command, Udid, bundleId); }
        catch (InvalidOperationException) { }
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"{name} is not set. Run the UI tests through scripts/ui-tests.sh, which builds the app and prepares the simulator and Appium.");
}
