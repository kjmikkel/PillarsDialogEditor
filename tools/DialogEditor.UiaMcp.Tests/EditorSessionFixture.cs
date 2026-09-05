// UseWPF/UseWindowsForms swap in the WPF implicit-usings set, which does NOT include
// System.IO. Same trap as the server project.
using System.IO;
using DialogEditor.UiaMcp;
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp.Tests;

/// <summary>
/// Launches the app once for a whole test class and tears it down after.
///
/// The settings snapshot goes to a TEST-SPECIFIC path, never the production one: these tests
/// must not touch the developer's own settings.json even if they crash mid-run. That is not
/// hypothetical — a leaked LastProjectPath from a crashed automation run is what motivated
/// SettingsGuard's startup recovery in the first place.
/// </summary>
public sealed class EditorSessionFixture : IDisposable
{
    // internal, not public: EditorSession is internal to the server assembly (reached here
    // via InternalsVisibleTo), and a public member cannot expose a less accessible type.
    // The fixture CLASS stays public because xunit only discovers public test classes.
    internal EditorSession Session { get; }

    public string RepoRoot { get; }

    public EditorSessionFixture()
    {
        RepoRoot = FindRepoRoot();

        var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PillarsDialogEditor", "settings.json");
        var backup = Path.Combine(Path.GetTempPath(),
            $"PillarsDialogEditor.settings.uiamcptests.{Guid.NewGuid():N}.json");

        Session = new EditorSession(new SettingsGuard(settings, backup));
        Session.Launch(RepoRoot, "none");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    public void Dispose()
    {
        try { Session.Kill(); } catch { /* best-effort teardown */ }
    }
}
