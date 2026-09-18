namespace AdyenOutLoud.ArchitectureTests;

/// <summary>Locates repository paths from the test run's output directory, independent of CI working directory.</summary>
internal static class RepoPaths
{
    public static string AppRoot { get; } = FindAppRoot();

    public static string Core => Path.Combine(AppRoot, "AdyenOutLoud.Core");

    public static string MauiApp => Path.Combine(AppRoot, "AdyenOutLoud");

    private static string FindAppRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AdyenOutLoud.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate AdyenOutLoud.slnx above " + AppContext.BaseDirectory);
    }
}
