using Xunit;

namespace AdyenOutLoud.ArchitectureTests;

/// <summary>
/// The MAUI head project's ViewModels are supposed to be plain C# orchestrating Core abstractions.
/// The multi-targeted head project (android/ios/maccatalyst/windows) cannot be safely loaded by
/// reflection from a plain net10.0 test host, so these rules are enforced by scanning source text instead.
/// </summary>
public sealed class MauiSourceBoundaryTests
{
    private static readonly string[] PlatformUsings =
    [
        "using Android.",
        "using Java.",
        "using UIKit;",
        "using UIKit.",
        "using ObjCRuntime",
        "using Foundation;",
        "using CoreFoundation",
        "using Microsoft.UI.",
        "using Microsoft.Maui.Platform",
        "using WinRT",
    ];

    [Fact]
    public void ViewModelsDoNotImportPlatformSpecificNamespaces()
    {
        var viewModelsDirectory = Path.Combine(RepoPaths.MauiApp, "ViewModels");
        Assert.True(Directory.Exists(viewModelsDirectory));

        foreach (var file in Directory.EnumerateFiles(viewModelsDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            var offending = lines.Where(line => PlatformUsings.Any(prefix => line.TrimStart().StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            Assert.True(offending.Length == 0,
                $"{Path.GetFileName(file)} imports platform-specific namespaces: {string.Join(", ", offending)}. " +
                "ViewModels must stay platform-agnostic; put platform calls behind a Core abstraction implemented in Services/.");
        }
    }

    [Fact]
    public void CodeBehindFilesDoNotConstructServicesDirectly()
    {
        // Views must receive their dependencies through constructor injection (see MauiProgram.cs),
        // never via `new SomeService()` in code-behind, which would bypass the DI container and hide the dependency.
        var forbiddenConstructors = new[]
        {
            "new RelayConnectionService(",
            "new PaymentAnnouncementService(",
            "new InstanceIdentityService(",
            "new ClientWebSocketConnection(",
            "new MauiSpeechService(",
            "new SecureStorageTokenStore(",
            "new PreferencesSettingsService(",
            "new ResxLocalizationService(",
        };

        foreach (var file in Directory.EnumerateFiles(RepoPaths.MauiApp, "*.xaml.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var offending = forbiddenConstructors.Where(text.Contains).ToArray();
            Assert.True(offending.Length == 0,
                $"{Path.GetFileName(file)} constructs services directly instead of using dependency injection: {string.Join(", ", offending)}.");
        }
    }
}
