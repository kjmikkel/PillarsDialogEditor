using System.Xml.Linq;

namespace DialogEditor.Tests.Accessibility;

/// <summary>
/// GitHub issue 260: Esc did nothing in most tool windows and in several dialogs, so a
/// keyboard user had to Tab to a Close button, or use Alt+F4 where there was none.
///
/// Every window except the two main windows needs exactly one Esc route:
/// <list type="bullet">
/// <item>a dialog: an <c>IsCancel="True"</c> button on its safe choice, so Esc runs that
///   button's own handler and sets the dialog's result;</item>
/// <item>a tool window: <c>shared:EscapeCloses.IsEnabled="True"</c> on the Window, which does
///   what the close box does;</item>
/// <item>or its own <c>Key.Escape</c> handling in code-behind.</item>
/// </list>
/// Two routes would race (EscapeCloses could close a dialog before its Cancel button sets the
/// result), so having both IsCancel and EscapeCloses is a failure too.
/// </summary>
public class EscapeRouteTests
{
    // Esc belongs to the canvas there (deselect, cancel a connection), not to closing the app.
    private static readonly HashSet<string> MainWindows = ["MainWindow.axaml"];

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static bool IsExcluded(string path, string root)
    {
        var segments = Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin") || segments.Contains("obj")
            || segments.Contains(".worktrees") || segments.Contains("worktrees")
            || segments.Contains("tools");
    }

    [Fact]
    public void EveryWindowHasExactlyOneEscapeRoute()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsExcluded(file, root) || MainWindows.Contains(Path.GetFileName(file))) continue;
            var doc = XDocument.Load(file);
            if (doc.Root?.Name.LocalName != "Window") continue;

            var cancel = doc.Descendants().Any(e => e.Attribute("IsCancel")?.Value == "True");
            var escapeCloses = doc.Root.Attributes().Any(a =>
                a.Name.LocalName == "EscapeCloses.IsEnabled" && a.Value == "True");
            var codeBehind = File.Exists(file + ".cs") && File.ReadAllText(file + ".cs").Contains("Key.Escape");
            // ConfirmDialog sets IsCancel from code, on whichever button is the safe one.
            var codeCancel = File.Exists(file + ".cs") && File.ReadAllText(file + ".cs").Contains("IsCancel = true");

            var routes = new[] { cancel || codeCancel, escapeCloses, codeBehind }.Count(r => r);
            if (routes == 0)
                offenders.Add($"{Path.GetFileName(file)}: no Esc route");
            else if (cancel && escapeCloses)
                offenders.Add($"{Path.GetFileName(file)}: both IsCancel and EscapeCloses (they race)");
        }

        Assert.True(offenders.Count == 0,
            "Every window needs one way to leave it with Esc (GitHub issue 260). Offenders:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// GitHub issue 260: these three had no Close button, so a keyboard user who didn't know
    /// Esc or Alt+F4 had no way out.
    /// </summary>
    [Theory]
    [InlineData("FindReplaceWindow.axaml")]
    [InlineData("BatchReplaceWindow.axaml")]
    [InlineData("FindInProjectWindow.axaml")]
    public void ToolWindowHasACloseButton(string window)
    {
        var doc = XDocument.Load(Path.Combine(SolutionRoot(), "DialogEditor.Avalonia", "Views", window));
        Assert.Contains(doc.Descendants(), e => e.Name.LocalName == "Button"
            && e.Attribute("Click")?.Value == "Close_Click"
            && (e.Attribute("Content")?.Value ?? "").EndsWith("_Close}"));
    }
}
