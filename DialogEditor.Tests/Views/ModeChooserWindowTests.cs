using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Views;

/// <summary>Issue 234: the first-launch mode chooser window. Layout is two panes side by
/// side, stacking when the window is too narrow for them.</summary>
public class ModeChooserWindowTests : IDisposable
{
    public ModeChooserWindowTests()
    {
        AppSettings.SettingsPathOverride = Path.GetTempFileName();
        Loc.Configure(new StubStringProvider());
    }

    public void Dispose()
    {
        // Close while the settings override is still set: closing without a pick saves Reading,
        // and a window left open could be closed later, into whatever file is current then.
        foreach (var w in _opened) w.Close();
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null) File.Delete(path);
    }

    private readonly List<ModeChooserWindow> _opened = [];

    private ModeChooserWindow Open()
    {
        var window = new ModeChooserWindow();
        _opened.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Window_HasBothPanes_SideBySide()
    {
        var window = Open();
        var reading = window.FindControl<Border>("ReadingPane")!;
        var editing = window.FindControl<Border>("EditingPane")!;

        Assert.True(reading.Bounds.X < editing.Bounds.X);
        Assert.Equal(reading.Bounds.Y, editing.Bounds.Y);
    }

    [AvaloniaFact]
    public void Window_StacksThePanes_WhenNarrow()
    {
        var window = Open();
        // Set directly (not via FontScale): the stacking decision is purely about width.
        window.MinWidth = 200;
        window.Width = 400;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var reading = window.FindControl<Border>("ReadingPane")!;
        var editing = window.FindControl<Border>("EditingPane")!;
        Assert.True(reading.Bounds.Y < editing.Bounds.Y);
        Assert.Equal(reading.Bounds.X, editing.Bounds.X);
    }

    [AvaloniaFact]
    public void Escape_ClosesAndSavesReading()
    {
        var window = Open();
        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(closed);
        Assert.Equal(EditorMode.Reading, AppSettings.Mode);
    }

    [AvaloniaFact]
    public void StartEditingButton_SavesEditing_AndCloses()
    {
        var window = Open();
        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.FindControl<Button>("EditingButton")!.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(closed);
        Assert.Equal(EditorMode.Editing, AppSettings.Mode);
    }

    [AvaloniaTheory]
    [InlineData("ReadingButton")]
    [InlineData("EditingButton")]
    public void Buttons_HaveTooltipsAndAutomationNames(string name)
    {
        var button = Open().FindControl<Button>(name)!;

        Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(button) as string));
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(button)));
        Assert.False(string.IsNullOrWhiteSpace(button.Content as string));
    }
}
