using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DialogEditor.Avalonia.Docking;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.Docking;

// Regression guard (GitHub issue 343): the test run must never touch the user's real dock
// layout. MainWindow saves the layout on close and deletes it when a restore fails or the
// user resets it, so every headless test that opens and closes a MainWindow with a built
// dock used to overwrite %LOCALAPPDATA%\PillarsDialogEditor\layout.json with the default.
public class DockLayoutIsolationTests
{
    private static readonly string RealLayoutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PillarsDialogEditor", "layout.json");

    private static DateTime? RealFileStamp() =>
        File.Exists(RealLayoutPath) ? File.GetLastWriteTimeUtc(RealLayoutPath) : null;

    // Same reflection setup as MainWindowPaneCycleTests.Open: a loaded game is what makes
    // BuildDock produce a DockLayout, and only a non-null DockLayout is saved on close.
    private static MainWindow OpenWithDock()
    {
        Loc.Configure(new StubStringProvider());
        var window = new MainWindow { Width = 1400, Height = 900 };
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();
        typeof(MainWindowViewModel).GetField("_activeGameId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, "poe2");
        typeof(MainWindowViewModel).GetMethod("RebuildConditionSearch", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, ["poe2"]);
        typeof(MainWindow).GetMethod("BuildDock", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [vm]);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(vm.DockLayout);
        return window;
    }

    [AvaloniaFact]
    public void OpenAndCloseMainWindow_DoesNotTouchTheRealLayoutFile()
    {
        var before = RealFileStamp();

        OpenWithDock().Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before, RealFileStamp());
    }

    // Without this the guard above would pass vacuously if the close stopped saving at all.
    [AvaloniaFact]
    public void OpenAndCloseMainWindow_SavesToTheRedirectedLayoutFile()
    {
        Assert.Equal(TestSettingsIsolation.RunLayoutPath, DockLayoutStore.DefaultPath);
        var before = File.Exists(DockLayoutStore.DefaultPath) ? File.GetLastWriteTimeUtc(DockLayoutStore.DefaultPath) : (DateTime?)null;

        OpenWithDock().Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(File.Exists(DockLayoutStore.DefaultPath));
        Assert.NotEqual(before, File.GetLastWriteTimeUtc(DockLayoutStore.DefaultPath));
    }
}
