using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Views;

namespace DialogEditor.Tests.Views;

public class UnsavedChangesDialogTests
{
    [AvaloniaFact]
    public void SaveButton_SetsResultToSave()
    {
        var dialog = new UnsavedChangesDialog("test_conv");
        dialog.Show();
        dialog.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(UnsavedChangesResult.Save, dialog.Result);
    }

    [AvaloniaFact]
    public void DiscardButton_SetsResultToDiscard()
    {
        var dialog = new UnsavedChangesDialog("test_conv");
        dialog.Show();
        dialog.FindControl<Button>("DiscardButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(UnsavedChangesResult.Discard, dialog.Result);
    }

    [AvaloniaFact]
    public void CancelButton_SetsResultToCancel()
    {
        var dialog = new UnsavedChangesDialog("test_conv");
        dialog.Show();
        dialog.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(UnsavedChangesResult.Cancel, dialog.Result);
    }

    [AvaloniaFact]
    public void MessageBlock_ContainsConversationName()
    {
        var dialog = new UnsavedChangesDialog("my_conv");
        dialog.Show();
        var text = dialog.FindControl<TextBlock>("MessageBlock")!.Text ?? string.Empty;
        Assert.Contains("my_conv", text);
    }
}
