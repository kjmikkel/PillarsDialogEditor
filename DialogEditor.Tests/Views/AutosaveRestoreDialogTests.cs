using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Views;

public class AutosaveRestoreDialogTests
{
    public AutosaveRestoreDialogTests() => Loc.Configure(new StubStringProvider());

    private static AutosaveGeneration Gen(string name, int minutesAgo, bool fallback = false)
        => new(name, DateTime.UtcNow.AddMinutes(-minutesAgo), fallback);

    private static void Click(Window dialog, string name)
        => dialog.FindControl<Button>(name)!.RaiseEvent(
            new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    // Issue 236: closing the window without a choice (title-bar X, Alt+F4) must never delete
    // the only copy of crashed work, so the default is "decide later", not "discard".
    [AvaloniaFact]
    public void Constructs_WithOneCopy_DefaultResultIsLater()
    {
        var dialog = new AutosaveRestoreDialog([Gen("a", 1)]);
        dialog.Show();
        Assert.True(dialog.IsVisible);
        Assert.Equal(AutosaveDecision.Later, dialog.Result);
        dialog.Close();
    }

    // Issue 36: someone who has just lost work shouldn't have to choose between timestamps to get
    // their editor back, so the earlier copies stay out of the way unless there are any.
    [AvaloniaFact]
    public void OneCopy_ShowsNoEarlierCopiesPicker()
    {
        var dialog = new AutosaveRestoreDialog([Gen("a", 1)]);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(dialog.FindControl<Expander>("EarlierCopies")!.IsVisible);
        dialog.Close();
    }

    [AvaloniaFact]
    public void SeveralCopies_PickerIsCollapsed_WithTheNewestPreselected()
    {
        var newest = Gen("a", 1);
        var dialog = new AutosaveRestoreDialog([newest, Gen("b", 6), Gen("c", 11)]);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        var expander = dialog.FindControl<Expander>("EarlierCopies")!;
        Assert.True(expander.IsVisible);
        Assert.False(expander.IsExpanded);
        var list = dialog.FindControl<ListBox>("CopyList")!;
        Assert.Equal(3, list.ItemCount);
        Assert.Same(newest, list.SelectedItem);
        dialog.Close();
    }

    [AvaloniaFact]
    public void Restore_ReturnsTheSelectedCopy()
    {
        var earlier = Gen("b", 6);
        var dialog = new AutosaveRestoreDialog([Gen("a", 1), earlier]);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        dialog.FindControl<ListBox>("CopyList")!.SelectedItem = earlier;

        Click(dialog, "RestoreButton");

        Assert.Equal(AutosaveDecision.Restore(earlier), dialog.Result);
    }

    // ── Issue 236: Esc is "decide later"; Discard asks first ─────────────────

    [AvaloniaFact]
    public void Esc_BelongsToDecideLater_NotToDiscard()
    {
        var dialog = new AutosaveRestoreDialog([Gen("a", 1)]);
        Assert.True(dialog.FindControl<Button>("LaterButton")!.IsCancel);
        Assert.False(dialog.FindControl<Button>("DiscardButton")!.IsCancel);
        Assert.False(dialog.FindControl<Button>("ConfirmDiscardButton")!.IsCancel);
    }

    [AvaloniaFact]
    public void DecideLater_ReturnsLater_AndCloses()
    {
        var dialog = new AutosaveRestoreDialog([Gen("a", 1)]);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        Click(dialog, "LaterButton");

        Assert.Equal(AutosaveDecision.Later, dialog.Result);
        Assert.False(dialog.IsVisible);
    }

    [AvaloniaFact]
    public void Discard_AsksToConfirm_BeforeDecidingAnything()
    {
        var dialog = new AutosaveRestoreDialog([Gen("a", 1), Gen("b", 6)]);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(dialog.FindControl<Control>("ConfirmDiscardPanel")!.IsVisible);

        Click(dialog, "DiscardButton");

        Assert.True(dialog.IsVisible);
        Assert.True(dialog.FindControl<Control>("ConfirmDiscardPanel")!.IsVisible);
        Assert.Equal(AutosaveDecision.Later, dialog.Result);
        dialog.Close();
    }

    [AvaloniaFact]
    public void ConfirmedDiscard_ReturnsDiscard()
    {
        var dialog = new AutosaveRestoreDialog([Gen("a", 1), Gen("b", 6)]);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        Click(dialog, "DiscardButton");
        Click(dialog, "ConfirmDiscardButton");

        Assert.Equal(AutosaveDecision.Discard, dialog.Result);
        Assert.False(dialog.IsVisible);
    }

    [AvaloniaFact]
    public void KeepThem_WithdrawsTheConfirmation_AndKeepsTheDialogOpen()
    {
        var dialog = new AutosaveRestoreDialog([Gen("a", 1)]);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        Click(dialog, "DiscardButton");
        Click(dialog, "KeepButton");

        Assert.True(dialog.IsVisible);
        Assert.False(dialog.FindControl<Control>("ConfirmDiscardPanel")!.IsVisible);
        Assert.Equal(AutosaveDecision.Later, dialog.Result);
        dialog.Close();
    }

    [AvaloniaFact]
    public void FallbackNewest_SaysWhereItWasKept()
    {
        var dialog = new AutosaveRestoreDialog([Gen(Path.Combine("x", "copy.autosave"), 1, fallback: true)]);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("AutosaveRestore_FromFallback", dialog.FindControl<TextBlock>("MessageText")!.Text);
        dialog.Close();
    }
}
