namespace DialogEditor.Tests.CanonicalMod;

/// Runs a scenario in a real editor session and copies the saved project (and its _vo/) to outDir.
public static class CanonicalModBuilder
{
    public const string ProjectName = "canonical_mod";

    public static async Task<string> BuildAsync(string game, IReadOnlyList<ScenarioStep> steps, string outDir)
    {
        using var session = await CanonicalModSession.OpenAsync(game, ProjectName);
        foreach (var step in steps) await step.Edit(session);
        session.Save();

        Directory.CreateDirectory(outDir);
        var projectDir = Path.GetDirectoryName(session.ProjectPath)!;
        foreach (var file in Directory.EnumerateFiles(projectDir, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(outDir, Path.GetRelativePath(projectDir, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
        return Path.Combine(outDir, ProjectName + ".dialogproject");
    }
}
