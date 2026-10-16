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

    [AvaloniaFact]
    public void Constructs_WithOneCopy_DefaultResultIsDiscard()
    {
        var dialog = new AutosaveRestoreDialog([Gen("a", 1)]);
        dialog.Show();
        Assert.True(dialog.IsVisible);
        Assert.Null(dialog.Result); // closing without a choice = discard the offer
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

        dialog.FindControl<Button>("RestoreButton")!.RaiseEvent(
            new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Same(earlier, dialog.Result);
    }

    [AvaloniaFact]
    public void Discard_ReturnsNoCopy()
    {
        var dialog = new AutosaveRestoreDialog([Gen("a", 1), Gen("b", 6)]);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        dialog.FindControl<Button>("DiscardButton")!.RaiseEvent(
            new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Null(dialog.Result);
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
