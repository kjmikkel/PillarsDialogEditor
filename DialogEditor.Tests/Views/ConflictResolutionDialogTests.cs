using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Shared;
using DialogEditor.Avalonia.Views;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Views;

public class ConflictResolutionDialogTests
{
    private static PatchConflictException MakeEx() =>
        new(42, "DefaultText", "old value", "new value");

    [AvaloniaFact]
    public void ForceButton_SetsForceApplyTrue()
    {
        var dialog = new ConflictResolutionDialog(MakeEx());
        dialog.Show();
        dialog.FindControl<Button>("ForceButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(dialog.ForceApply);
    }

    [AvaloniaFact]
    public void CancelButton_LeavesForceApplyFalse()
    {
        var dialog = new ConflictResolutionDialog(MakeEx());
        dialog.Show();
        dialog.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(dialog.ForceApply);
    }

    [AvaloniaFact]
    public void Constructor_PopulatesDetailFields()
    {
        var dialog = new ConflictResolutionDialog(MakeEx());
        dialog.Show();
        Assert.Equal("42",        dialog.FindControl<TextBlock>("NodeIdBlock")!.Text);
        Assert.Equal("DefaultText", dialog.FindControl<TextBlock>("FieldNameBlock")!.Text);
        Assert.Equal("old value", dialog.FindControl<TextBlock>("ExpectedBlock")!.Text);
        Assert.Equal("new value", dialog.FindControl<TextBlock>("ActualBlock")!.Text);
    }

    [AvaloniaFact]
    public void Tab_ToControlWithHelpText_UpdatesHintBar()
    {
        var dialog = new ConflictResolutionDialog(MakeEx());
        dialog.Show();

        var button = dialog.FindControl<Button>("ForceButton")!;
        var expectedHint = AutomationProperties.GetHelpText(button);
        Assert.False(string.IsNullOrEmpty(expectedHint));

        button.RaiseEvent(new GotFocusEventArgs
        {
            RoutedEvent = InputElement.GotFocusEvent,
            NavigationMethod = NavigationMethod.Tab,
        });

        Assert.Equal(expectedHint, dialog.FindControl<FocusHintBar>("HintBar")!.Text);
    }
}
