using System.Diagnostics;

namespace AdyenOutLoud.UITests;

/// <summary>Thin wrapper over <c>xcrun simctl</c> for the few device-level operations the tests need.</summary>
internal static class Simulator
{
    public static async Task<string> SimctlAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("xcrun") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("simctl");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start xcrun.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"simctl {string.Join(' ', arguments)} failed ({process.ExitCode}): {await error}");
        }

        return await output;
    }
}
