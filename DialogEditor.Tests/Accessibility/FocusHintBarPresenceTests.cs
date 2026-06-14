using System.Xml.Linq;

namespace DialogEditor.Tests.Accessibility;

/// <summary>
/// Gaps.md a11y item 13: each of the 10 "workhorse" secondary windows must carry a
/// &lt;shared:FocusHintBar x:Name="HintBar"/&gt; (see design spec
/// docs/superpowers/specs/2026-06-13-focus-hint-bar-design.md Part 2). Solution-wide
/// scan anchored on DialogEditor.slnx, mirroring AutomationHelpTextTests/
/// FakeWatermarkTests' pattern — a future accidental removal fails this too.
/// </summary>
public class FocusHintBarPresenceTests
{
    private static readonly string[] WindowsInScope =
    {
        "SettingsWindow.axaml",
        "ScriptEditorWindow.axaml",
        "ConditionEditorWindow.axaml",
        "FindReplaceWindow.axaml",
        "DiffWindow.axaml",
        "BatchReplaceWindow.axaml",
        "ExportConversationsWindow.axaml",
        "FlowAnalyticsWindow.axaml",
        "BranchesWindow.axaml",
        "GitConflictResolutionWindow.axaml",
    };

    /// <summary>
    /// Gaps.md a11y item 16: of the 7 small 1-3-control dialogs left out of item 13,
    /// only these 3 have at least one AutomationProperties.HelpText value that adds
    /// information beyond text already visible in the dialog (see design spec
    /// docs/superpowers/specs/2026-06-13-focus-hint-bar-small-dialogs-design.md). The
    /// other 4 (BranchNameDialog, CommitConsentDialog, ChangelogWindow,
    /// ForceDeleteDialog) deliberately do NOT get a FocusHintBar — their HelpText
    /// duplicates visible text, so a bar would only echo the screen.
    /// </summary>
    private static readonly string[] WindowsInScopeItem16 =
    {
        "AboutWindow.axaml",
        "ConflictResolutionDialog.axaml",
        "HistoryWindow.axaml",
    };

    private static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // Checked relative to root, not the absolute path, so this doesn't misfire when root
    // itself lives under a "worktrees" directory (e.g. when running from inside
    // .claude/worktrees/<name>).
    private static bool IsExcluded(string path, string root)
    {
        var segments = Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin") || segments.Contains("obj")
            || segments.Contains(".worktrees") || segments.Contains("worktrees");
    }

    private static void AssertHasFocusHintBar(string fileName)
    {
        var root = SolutionRoot();
        var matches = Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .Where(f => !IsExcluded(f, root))
            .ToList();

        Assert.Single(matches);
        var doc = XDocument.Load(matches[0]);

        var hasHintBar = doc.Descendants()
            .Where(e => e.Name.LocalName == "FocusHintBar")
            .Any(e => e.Attribute(XamlNs + "Name")?.Value == "HintBar");

        Assert.True(hasHintBar, $"{fileName} is missing <shared:FocusHintBar x:Name=\"HintBar\"/>");
    }

    public static IEnumerable<object[]> WindowFiles() => WindowsInScope.Select(f => new object[] { f });

    public static IEnumerable<object[]> WindowFilesItem16() => WindowsInScopeItem16.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(WindowFiles))]
    public void WindowHasFocusHintBar(string fileName) => AssertHasFocusHintBar(fileName);

    [Theory]
    [MemberData(nameof(WindowFilesItem16))]
    public void Item16WindowHasFocusHintBar(string fileName) => AssertHasFocusHintBar(fileName);
}
