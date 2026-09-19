using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AdyenOutLoud.TestSupport;

/// <summary>
/// Runs the real Cloudflare Worker locally (<c>wrangler dev</c>, i.e. workerd) for the whole test run
/// and offers the HTTP operation a test needs: post a webhook.
/// </summary>
public sealed class LocalWorker : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);
    private readonly StringBuilder _log = new();
    private readonly string _stateDirectory = Path.Combine(Path.GetTempPath(), $"adyen-e2e-{Guid.NewGuid():N}");
    private Process? _process;

    public Uri BaseUri { get; private set; } = null!;

    public HttpClient Http { get; } = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task InitializeAsync()
    {
        var wrangler = Path.Combine(RepositoryPaths.Worker, "node_modules", "wrangler", "bin", "wrangler.js");
        if (!File.Exists(wrangler))
        {
            throw new InvalidOperationException("Wrangler is not installed. Run `npm ci` in worker/ before the E2E tests.");
        }

        var port = FreePort();
        BaseUri = new Uri($"http://127.0.0.1:{port}");
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = RepositoryPaths.Worker,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { wrangler, "dev", "--port", port.ToString(CultureInfo.InvariantCulture), "--ip", "127.0.0.1", "--inspector-port", FreePort().ToString(CultureInfo.InvariantCulture), "--persist-to", _stateDirectory, "--var", "ADYEN_WEBHOOK_HOST:" })
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["WRANGLER_SEND_METRICS"] = "false";
        start.Environment["CI"] = "1";

        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start `node`.");
        _process.OutputDataReceived += (_, e) => Append(e.Data);
        _process.ErrorDataReceived += (_, e) => Append(e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitUntilHealthyAsync();
    }

    public Task DisposeAsync()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }

        _process?.Dispose();
        Http.Dispose();
        try { Directory.Delete(_stateDirectory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return Task.CompletedTask;
    }

    /// <summary>The webhook URL an operator would configure in the Adyen Customer Area (local http here).</summary>
    public Uri WebhookUrl => new(BaseUri, "/webhook");

    public async Task<HttpResponseMessage> PostWebhookAsync(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await Http.PostAsync(WebhookUrl, content);
    }

    /// <summary>Returns the last output of the wrangler process, for failure diagnostics.</summary>
    public string Log
    {
        get { lock (_log) { return _log.ToString(); } }
    }

    private void Append(string? line)
    {
        if (line is null) return;
        lock (_log) { _log.AppendLine(line); }
    }

    private async Task WaitUntilHealthyAsync()
    {
        var deadline = DateTime.UtcNow + StartupTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_process!.HasExited)
            {
                throw new InvalidOperationException($"`wrangler dev` exited with code {_process.ExitCode}:{Environment.NewLine}{Log}");
            }

            try
            {
                using var response = await Http.GetAsync(new Uri(BaseUri, "/health"));
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            await Task.Delay(250);
        }

        throw new TimeoutException($"The local Worker did not become healthy within {StartupTimeout.TotalSeconds:0}s:{Environment.NewLine}{Log}");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
