using System.Text;
using DialogEditor.Avalonia.Services;

namespace DialogEditor.Tests.CanonicalMod;

/// Runs a scenario in a real editor session and writes the canonical mod (issue 138): the
/// saved project and its _vo/, the .dialogpack built by the editor's own exporter, and a
/// README generated from the scenario steps, so the feature map can't drift from the mod.
public static class CanonicalModBuilder
{
    public const string ProjectName = "canonical_mod";

    /// The checked-in copy in the source tree (not the test output folder).
    public static string GoldenDir(string game)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "DialogEditor.Tests", "Fixtures", "CanonicalMod", game);
    }

    public static async Task<string> BuildAsync(string game, IReadOnlyList<ScenarioStep> steps, string outDir)
    {
        using var session = new CanonicalModSession();
        await session.LoadNewProjectAsync(game, ProjectName);
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

    public static async Task BuildAllAsync(string game, IReadOnlyList<ScenarioStep> steps, string outDir)
    {
        var project = await BuildAsync(game, steps, outDir);
        await VoPackExporter.ExportAsync(project, Path.Combine(outDir, ProjectName + ".dialogpack"));
        File.WriteAllText(Path.Combine(outDir, "README.md"), RenderReadme(game, steps), new UTF8Encoding(false));
    }

    // "\n" throughout, so the output is the same on every OS.
    private static string RenderReadme(string game, IReadOnlyList<ScenarioStep> steps)
    {
        var sb = new StringBuilder();
        sb.Append($"# Canonical mod — {game}\n\n");
        sb.Append("**Generated — do not edit by hand.** The scenario in `DialogEditor.Tests/CanonicalMod/` builds\n");
        sb.Append("this mod through the editor's own view models ([#138](https://github.com/kjmikkel/PillarsDialogEditor/issues/138)).\n");
        sb.Append("`CanonicalModGoldenTests` fails when a fresh build differs from these files. If the editor's\n");
        sb.Append("output changed on purpose, run that test with `DIALOGEDITOR_REGENERATE_CANONICAL_MOD=1`, review\n");
        sb.Append("the diff and commit it.\n\n");
        sb.Append($"The mod edits the `canonical` conversation of [`../../Canonical/{game}`](../../Canonical/{game}) and adds a\n");
        sb.Append("new one. To try it in the app, copy that fixture folder somewhere outside the repo, choose the copy as\n");
        sb.Append("the game folder, and apply `canonical_mod.dialogpack` with the Patch Manager. All text, GUIDs,\n");
        sb.Append("variable names and voice-over bytes are invented; nothing comes from game data.\n\n");
        sb.Append("## Features\n\n");
        sb.Append("Each row is one scenario step. Its expectation is checked after applying the mod through\n");
        sb.Append("`dialog-patcher`, the Patch Manager and Test Patch (F5).\n\n");
        sb.Append("| Feature | What the mod does | Known issue |\n|---|---|---|\n");
        foreach (var s in steps)
            sb.Append($"| `{s.Tag}` | {s.Description.Replace("|", "\\|")} | {s.KnownIssue ?? ""} |\n");
        return sb.ToString();
    }
}
