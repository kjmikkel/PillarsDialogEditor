using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Views;

/// <summary>
/// GitHub issue 243: MainWindow handles Ctrl+Z / Ctrl+Y on the tunnel route for canvas
/// undo. In a text box those keys must reach the text box, or Ctrl+Z undoes the last graph
/// edit instead of the typing (typing only joins the canvas history when the field loses focus).
/// </summary>
public class MainWindowTextBoxUndoTests : IDisposable
{
    private readonly string _settingsPath;

    public MainWindowTextBoxUndoTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwtu_settings_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    // The dock doesn't realise its panes headless, so a text box is added to the window's own
    // tree; keys then go through the real input pipeline (tunnel route included).
    private static (MainWindow window, TextBox box) ShowWithFocusedTextBox()
    {
        var window = new MainWindow();
        window.Show();
        var box = new TextBox();
        window.GetVisualDescendants().OfType<Panel>().First().Children.Add(box);
        Assert.True(box.Focus());
        return (window, box);
    }

    [AvaloniaFact]
    public void CtrlZ_InTextBox_UndoesTheTyping_CtrlY_RedoesIt()
    {
        var (window, box) = ShowWithFocusedTextBox();
        window.KeyTextInput("abc");
        Assert.Equal("abc", box.Text);

        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
        Assert.NotEqual("abc", box.Text);

        window.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, "y");
        Assert.Equal("abc", box.Text);
    }
}
