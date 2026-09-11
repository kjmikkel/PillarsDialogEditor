namespace DialogEditor.Tests.Helpers;

/// <summary>Locates the repository root from the test binary's output directory.</summary>
public static class TestPaths
{
    public static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("DialogEditor.slnx not found above the test output directory.");
        return dir.FullName;
    }
}
