using System.Text.RegularExpressions;
using Xunit;

namespace AdyenOutLoud.ArchitectureTests;

/// <summary>Parses every .csproj under app/ and checks the reference graph, independent of which projects happen to build on this host.</summary>
public sealed partial class ProjectReferenceGraphTests
{
    private static Dictionary<string, string[]> BuildGraph()
    {
        var graph = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var csproj in Directory.EnumerateFiles(RepoPaths.AppRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var projectName = Path.GetFileNameWithoutExtension(csproj);
            var text = File.ReadAllText(csproj);
            var references = ProjectReferencePattern().Matches(text)
                .Select(match => Path.GetFileNameWithoutExtension(match.Groups[1].Value))
                .ToArray();
            graph[projectName] = references;
        }

        return graph;
    }

    [Fact]
    public void NoProductionProjectReferencesTheTestProjects()
    {
        var testProjects = new[] { "AdyenOutLoud.Tests", "AdyenOutLoud.ArchitectureTests" };
        var graph = BuildGraph();

        foreach (var (project, references) in graph)
        {
            if (testProjects.Contains(project, StringComparer.OrdinalIgnoreCase)) continue;

            var forbidden = references.Where(reference => testProjects.Contains(reference, StringComparer.OrdinalIgnoreCase)).ToArray();
            Assert.True(forbidden.Length == 0,
                $"Production project '{project}' references test project(s): {string.Join(", ", forbidden)}.");
        }
    }

    [Fact]
    public void TheProjectReferenceGraphHasNoCycles()
    {
        var graph = BuildGraph();
        foreach (var project in graph.Keys)
        {
            var path = new List<string>();
            Assert.False(HasCycle(project, graph, path, new HashSet<string>(StringComparer.OrdinalIgnoreCase)),
                $"Circular project reference detected: {string.Join(" -> ", path)} -> {project}.");
        }
    }

    [Fact]
    public void CoreHasNoProjectReferencesAtAll()
    {
        var graph = BuildGraph();
        Assert.True(graph.TryGetValue("AdyenOutLoud.Core", out var references));
        Assert.Empty(references);
    }

    private static bool HasCycle(string project, Dictionary<string, string[]> graph, List<string> path, HashSet<string> visiting)
    {
        if (path.Contains(project, StringComparer.OrdinalIgnoreCase)) return true;
        if (!visiting.Add(project)) return false;

        path.Add(project);
        if (graph.TryGetValue(project, out var references))
        {
            foreach (var reference in references)
            {
                if (HasCycle(reference, graph, path, visiting)) return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    [GeneratedRegex("<ProjectReference\\s+Include=\"([^\"]+)\"")]
    private static partial Regex ProjectReferencePattern();
}
