using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Views;

namespace DialogEditor.Tests.Views;

public class LanguageCodeDialogTests
{
    [AvaloniaFact]
    public void OkButton_EmptyText_ResultIsNull()
    {
        var dialog = new LanguageCodeDialog(string.Empty);
        dialog.Show();
        dialog.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(dialog.Result);
    }

    [AvaloniaFact]
    public void OkButton_WhitespaceText_ResultIsNull()
    {
        var dialog = new LanguageCodeDialog("   ");
        dialog.Show();
        dialog.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(dialog.Result);
    }

    [AvaloniaFact]
    public void OkButton_PaddedLanguageCode_ResultIsTrimmed()
    {
        var dialog = new LanguageCodeDialog("  en-US  ");
        dialog.Show();
        dialog.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("en-US", dialog.Result);
    }
}
