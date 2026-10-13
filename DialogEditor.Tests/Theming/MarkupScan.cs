using System.Xml.Linq;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Every element of every .axaml file in the solution, for markup guard tests (issues 179, 180).
/// Build output and worktrees are skipped.
/// </summary>
internal static class MarkupScan
{
    public static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    public static IEnumerable<(string File, XElement Element)> Elements()
    {
        var root = SolutionRoot();
        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            var segments = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Intersect(["bin", "obj", ".worktrees", "worktrees", ".claude"]).Any()) continue;
            foreach (var el in XDocument.Load(file).Descendants())
                yield return (Path.GetFileName(file), el);
        }
    }
}
