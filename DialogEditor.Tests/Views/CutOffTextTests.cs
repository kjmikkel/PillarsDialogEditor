using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Views;

/// <summary>
/// GitHub issue 250: three single-line texts were clipped at the window or field edge with no
/// way to read the rest. Each now wraps. A tooltip would not do: these hints exist for keyboard
/// users, who can't hover.
/// </summary>
public class CutOffTextTests : IDisposable
{
    private const string Long =
        "Drag a node to move it. Right-click the canvas to add a node or an annotation, " +
        "and press Enter on a selected node to edit its text in Node Details.";

    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"cut_settings_{Guid.NewGuid():N}.json");

    public CutOffTextTests()
    {
        Loc.Configure(new StubStringProvider());
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    private static int Lines(TextBlock block) => block.TextLayout.TextLines.Count;

    [AvaloniaFact]
    public void FocusHintBar_WrapsALongHint()
    {
        var bar = new FocusHintBar();
        var window = new Window { Width = 300, Height = 200, Content = bar };
        window.Show();
        bar.Text = Long;
        Dispatcher.UIThread.RunJobs();

        Assert.True(Lines(bar.FindControl<TextBlock>("HintText")!) > 1);
    }

    [AvaloniaFact]
    public void MainWindowStatusBar_WrapsALongHint()
    {
        var window = new MainWindow { Width = 500 };
        window.Show();
        ((MainWindowViewModel)window.DataContext!).FocusHintText = Long;
        Dispatcher.UIThread.RunJobs();

        var status = window.FindControl<TextBlock>("StatusDisplay");
        Assert.NotNull(status);
        Assert.True(Lines(status!) > 1);
    }

    [AvaloniaFact]
    public void SettingsBackupDirectory_WrapsALongPath()
    {
        var vm = new SettingsViewModel("/game", new StubFolderPicker())
        {
            BackupDirectory = string.Join("/", Enumerable.Repeat("a-rather-long-folder-name", 12)),
        };
        var window = new SettingsWindow { DataContext = vm, Width = 500 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var box = window.GetVisualDescendants().OfType<TextBox>().First(t => t.Classes.Contains("path"));
        var presenter = box.GetVisualDescendants().OfType<TextPresenter>().First();
        Assert.True(presenter.TextLayout.TextLines.Count > 1);
    }
}
