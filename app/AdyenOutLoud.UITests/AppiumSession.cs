using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace AdyenOutLoud.UITests;

/// <summary>
/// A minimal W3C WebDriver client for the Appium server — just the handful of commands these tests need,
/// so the suite has no client-library dependency beyond the Appium server itself.
/// </summary>
internal sealed class AppiumSession : IAsyncDisposable
{
    private const string ElementKey = "element-6066-11e4-a52e-4f735466cecf";
    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(20);
    private readonly HttpClient _http;
    private readonly string _session;

    private AppiumSession(HttpClient http, string session)
    {
        _http = http;
        _session = session;
    }

    public static async Task<AppiumSession> StartAsync(Uri server, string udid, string bundleId)
    {
        var http = new HttpClient { BaseAddress = server, Timeout = TimeSpan.FromMinutes(6) };
        var capabilities = new
        {
            capabilities = new
            {
                alwaysMatch = new Dictionary<string, object>
                {
                    ["platformName"] = "iOS",
                    ["appium:automationName"] = "XCUITest",
                    ["appium:udid"] = udid,
                    ["appium:bundleId"] = bundleId,
                    ["appium:noReset"] = true,
                    ["appium:wdaLaunchTimeout"] = 300000,
                    ["appium:shouldTerminateApp"] = true,
                },
            },
        };
        using var response = await http.PostAsJsonAsync("session", capabilities);
        var value = await ReadValueAsync(response);
        return new AppiumSession(http, value.GetProperty("sessionId").GetString()!);
    }

    /// <summary>Finds an element by accessibility id (the MAUI AutomationId, or SemanticProperties.Description), waiting for it to appear.</summary>
    public async Task<string> FindAsync(string accessibilityId, TimeSpan? timeout = null)
    {
        string? found = null;
        await WaitUntilAsync(async () =>
        {
            found = await TryFindAsync(accessibilityId);
            return found is not null;
        }, timeout ?? DefaultWait, $"element '{accessibilityId}' to appear");
        return found!;
    }

    public async Task TapElementAsync(string element) => await PostAsync($"element/{element}/click", new { });

    public async Task<bool> ExistsAsync(string accessibilityId) => await TryFindAsync(accessibilityId) is not null;

    public async Task TapAsync(string accessibilityId) => await PostAsync($"element/{await FindAsync(accessibilityId)}/click", new { });

    /// <summary>Replaces the contents of a text field. With <paramref name="pressReturn"/> the keyboard is dismissed afterwards.</summary>
    public async Task TypeAsync(string accessibilityId, string text, bool pressReturn = false)
    {
        var element = await FindAsync(accessibilityId);
        await PostAsync($"element/{element}/click", new { });
        await PostAsync($"element/{element}/clear", new { });
        await PostAsync($"element/{element}/value", new { text = pressReturn ? text + "\n" : text });
    }

    /// <summary>Reads an element attribute: <c>label</c> for text labels, <c>value</c> for inputs and the picker.</summary>
    public async Task<string> AttributeAsync(string accessibilityId, string attribute)
    {
        var element = await FindAsync(accessibilityId);
        var value = await GetAsync($"element/{element}/attribute/{attribute}");
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;
    }

    public Task<string> LabelAsync(string accessibilityId) => AttributeAsync(accessibilityId, "label");

    public Task<string> ValueAsync(string accessibilityId) => AttributeAsync(accessibilityId, "value");

    public async Task WaitForLabelAsync(string accessibilityId, Func<string, bool> predicate, string description, TimeSpan? timeout = null)
    {
        try
        {
            await WaitUntilAsync(async () => predicate(await LabelAsync(accessibilityId)), timeout ?? DefaultWait, description);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException($"{exception.Message} Last {accessibilityId} was '{await SafeLabelAsync(accessibilityId)}'.", exception);
        }
    }

    public async Task ExecuteMobileAsync(string command, object arguments) =>
        await PostAsync("execute/sync", new { script = $"mobile: {command}", args = new[] { arguments } });

    public async Task<string> FindByClassChainAsync(string classChain, TimeSpan? timeout = null)
    {
        string? found = null;
        await WaitUntilAsync(async () =>
        {
            var result = await PostAsync("elements", new { @using = "-ios class chain", value = classChain });
            found = result.GetArrayLength() > 0 ? result[0].GetProperty(ElementKey).GetString() : null;
            return found is not null;
        }, timeout ?? DefaultWait, $"element '{classChain}' to appear");
        return found!;
    }

    public async Task SetValueAsync(string element, string text) => await PostAsync($"element/{element}/value", new { text });

    public async Task<byte[]> ScreenshotAsync() => Convert.FromBase64String((await GetAsync("screenshot")).GetString()!);

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (await condition()) return;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0}s waiting for {description}.");
            await Task.Delay(250);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { using var response = await _http.DeleteAsync($"session/{_session}"); }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        _http.Dispose();
    }

    private async Task<string?> SafeLabelAsync(string accessibilityId)
    {
        try { return await LabelAsync(accessibilityId); }
        catch (Exception exception) when (exception is TimeoutException or HttpRequestException) { return null; }
    }

    private async Task<string?> TryFindAsync(string accessibilityId)
    {
        using var response = await _http.PostAsJsonAsync($"session/{_session}/elements", new { @using = "accessibility id", value = accessibilityId });
        var value = await ReadValueAsync(response);
        return value.GetArrayLength() > 0 ? value[0].GetProperty(ElementKey).GetString() : null;
    }

    private async Task<JsonElement> GetAsync(string path)
    {
        using var response = await _http.GetAsync($"session/{_session}/{path}");
        return await ReadValueAsync(response);
    }

    private async Task<JsonElement> PostAsync(string path, object body)
    {
        using var response = await _http.PostAsJsonAsync($"session/{_session}/{path}", body);
        return await ReadValueAsync(response);
    }

    private static async Task<JsonElement> ReadValueAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(string.Create(CultureInfo.InvariantCulture, $"Appium returned {(int)response.StatusCode}: {text}"));
        }

        return JsonDocument.Parse(text).RootElement.GetProperty("value").Clone();
    }
}
