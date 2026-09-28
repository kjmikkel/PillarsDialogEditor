using System.Text.RegularExpressions;
using DialogEditor.PatchCli;

namespace DialogEditor.Tests.PatchCli;

/// <summary>
/// docs/patcher/README.md is the player guide that ships in the patcher zip and that
/// About > Player guide opens on GitHub (issue #80). These keep it from drifting away from
/// what ships beside it.
/// </summary>
public class PatcherReadmeTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadmePath => Path.Combine(SolutionRoot(), "docs", "patcher", "README.md");

    [Fact]
    public void EveryImageTheGuideShows_Exists()
    {
        // build-dist.ps1 copies docs/patcher/images into the zip beside the README, so a
        // missing file is a broken picture both on GitHub and on the player's disk.
        var readme = File.ReadAllText(ReadmePath);
        var images = Regex.Matches(readme, @"!\[[^\]]*\]\(([^)]+)\)").Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(images);
        var missing = images.Where(i => !File.Exists(Path.Combine(SolutionRoot(), "docs", "patcher", i))).ToList();
        Assert.True(missing.Count == 0, "Missing images: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryCommandLineOption_IsInTheGuide()
    {
        var help = new StringWriter();
        PatcherCommand.Run(["--help"], help, new StringWriter());
        var options = Regex.Matches(help.ToString(), @"(?<![\w-])--[a-z][a-z-]+").Select(m => m.Value).Distinct().ToList();

        var readme  = File.ReadAllText(ReadmePath);
        var missing = options.Where(o => !readme.Contains($"`{o}`")).ToList();

        Assert.NotEmpty(options);
        Assert.True(missing.Count == 0, "Options missing from the player guide: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryExitCode_IsInTheGuide()
    {
        var help = new StringWriter();
        PatcherCommand.Run(["--help"], help, new StringWriter());
        var exitSection = help.ToString()[help.ToString().IndexOf("Exit codes:", StringComparison.Ordinal)..];
        var codes = Regex.Matches(exitSection, @"^\s+(\d)\s", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();

        var readme = File.ReadAllText(ReadmePath);

        Assert.NotEmpty(codes);
        Assert.All(codes, c => Assert.Contains($"| `{c}` |", readme));
    }
}
