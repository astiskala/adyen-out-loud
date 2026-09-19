namespace AdyenOutLoud.TestSupport;

internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string Worker => Path.Combine(Root, "worker");

    public static string Fixture(string name) => Path.Combine(Worker, "test", "fixtures", name);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find the repository root (global.json) above the test output directory.");
    }
}
