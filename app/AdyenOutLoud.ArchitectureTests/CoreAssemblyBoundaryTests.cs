using System.Reflection;
using Xunit;

namespace AdyenOutLoud.ArchitectureTests;

/// <summary>
/// Core must stay usable from a plain console app or a unit test host: no MAUI, no platform SDKs.
/// This is enforced by inspecting the compiled assembly rather than a third-party architecture-testing
/// package, because the rule set is small and stable enough that reflection is the higher-signal, lower-dependency choice.
/// </summary>
public sealed class CoreAssemblyBoundaryTests
{
    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "Microsoft.Maui",
        "Xamarin",
        "Android",
        "Mono.Android",
        "Java.Interop",
        "UIKit",
        "ObjCRuntime",
        "Foundation",
        "CoreFoundation",
        "WinRT",
        "Microsoft.WindowsAppSDK",
        "Windows",
        "Microsoft.UI",
    ];

    private static Assembly CoreAssembly { get; } = typeof(AdyenOutLoud.Models.PaymentMessage).Assembly;

    [Fact]
    public void CoreDoesNotReferenceAnyMauiOrPlatformAssembly()
    {
        var offending = CoreAssembly.GetReferencedAssemblies()
            .Where(reference => ForbiddenAssemblyPrefixes.Any(prefix =>
                reference.Name is not null && reference.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(reference => reference.Name)
            .ToArray();

        Assert.True(offending.Length == 0,
            $"AdyenOutLoud.Core referenced platform assemblies: {string.Join(", ", offending)}. " +
            "Core must remain usable outside MAUI (see AGENTS.md architecture rules).");
    }

    [Fact]
    public void CoreDeclaresNoTypesInPlatformNamespaces()
    {
        var offending = CoreAssembly.GetTypes()
            .Where(type => type.Namespace is not null && ForbiddenAssemblyPrefixes.Any(prefix =>
                type.Namespace!.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(type => type.FullName)
            .ToArray();

        Assert.True(offending.Length == 0,
            $"AdyenOutLoud.Core declared types in platform namespaces: {string.Join(", ", offending)}.");
    }

    [Fact]
    public void CoreAssemblyNameIsStableForDownstreamConsumers()
    {
        Assert.Equal("AdyenOutLoud.Core", CoreAssembly.GetName().Name);
    }
}
