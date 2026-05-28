using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Views;

namespace DialogEditor.Tests.Views;

public class ConversationNameDialogTests
{
    [AvaloniaFact]
    public void OkButton_ReturnsTypedText()
    {
        var dialog = new ConversationNameDialog();
        dialog.Show();
        dialog.FindControl<TextBox>("NameBox")!.Text = "my_conv";
        dialog.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("my_conv", dialog.Result);
    }

    [AvaloniaFact]
    public void CancelButton_ResultIsNull()
    {
        var dialog = new ConversationNameDialog();
        dialog.Show();
        dialog.FindControl<TextBox>("NameBox")!.Text = "ignored";
        dialog.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(dialog.Result);
    }

    [AvaloniaFact]
    public void DefaultValue_PrefillsNameBox()
    {
        var dialog = new ConversationNameDialog("city_market");
        dialog.Show();
        Assert.Equal("city_market", dialog.FindControl<TextBox>("NameBox")!.Text);
    }

    [AvaloniaFact]
    public void EnterKey_AcceptsAndSetsResult()
    {
        var dialog = new ConversationNameDialog();
        dialog.Show();
        var box = dialog.FindControl<TextBox>("NameBox")!;
        box.Text = "enter_conv";
        box.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter
        });
        Assert.Equal("enter_conv", dialog.Result);
    }

    [AvaloniaFact]
    public void EscapeKey_CancelsAndResultIsNull()
    {
        var dialog = new ConversationNameDialog();
        dialog.Show();
        var box = dialog.FindControl<TextBox>("NameBox")!;
        box.Text = "ignored";
        box.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Escape
        });
        Assert.Null(dialog.Result);
    }
}
