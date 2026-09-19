using ObjCRuntime;
using UIKit;

namespace AdyenOutLoud;

/// <summary>
/// Mac Catalyst program entry point.
/// </summary>
public static class Program
{
    /// <summary>
    /// Main entry point for Mac Catalyst.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
