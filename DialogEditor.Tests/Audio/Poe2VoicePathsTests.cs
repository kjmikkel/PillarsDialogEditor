using DialogEditor.Core.Audio;

namespace DialogEditor.Tests.Audio;

/// The PoE2 voice-over root has one definition (issue 101). The game reads conversation
/// and chatter VO only from Voices/English(US) whatever the text language, so there is
/// one root, and a second copy of the path could only drift from it.
public class Poe2VoicePathsTests
{
    [Fact]
    public void VoicesRoot_IsTheEnglishUsVoicesFolderUnderStreamingAssets()
    {
        var gameDir = Path.Combine("games", "deadfire");

        Assert.Equal(
            Path.Combine(gameDir, "PillarsOfEternityII_Data", "StreamingAssets",
                         "Audio", "Windows", "Voices", "English(US)"),
            Poe2VoicePaths.VoicesRoot(gameDir));
    }

    [Fact]
    public void TheVoiceLanguageFolderIsNamedOnlyInPoe2VoicePaths()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (IsSkipped(file)) continue;
            if (Path.GetFileName(file) == "Poe2VoicePaths.cs") continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (lines[i].Contains("\"English(US)\"", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(offenders.Count == 0,
            "Build the PoE2 VO root with Poe2VoicePaths.VoicesRoot, not by hand. Offenders:\n"
            + string.Join("\n", offenders));
    }

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // Build output, the Tests project (fixtures name the folder on purpose) and other
    // branches' working copies under .worktrees/ or .claude/worktrees/. Segments are taken
    // relative to the root so a root that itself sits under "worktrees" still scans.
    private static bool IsSkipped(string file)
    {
        var segments = Path.GetRelativePath(SolutionRoot(), file).Split(Path.DirectorySeparatorChar);
        return segments.Contains("bin") || segments.Contains("obj")
            || segments.Contains("DialogEditor.Tests")
            || segments.Contains(".worktrees") || segments.Contains("worktrees");
    }
}
