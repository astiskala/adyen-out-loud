using Foundation;

namespace AdyenOutLoud;

/// <summary>
/// iOS application delegate.
/// </summary>
[Register("AppDelegate")]
public class AppHost : MauiUIApplicationDelegate
{
    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
