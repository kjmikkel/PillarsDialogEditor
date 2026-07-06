using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Behaviors;

namespace DialogEditor.Tests.Views;

public class TokenCompletionBehaviorTests
{
    private static TextBox Setup(string gameId = "poe2")
    {
        var tb = new TextBox { AcceptsReturn = true };
        TokenCompletion.SetGameId(tb, gameId);
        TokenCompletion.SetIsEnabled(tb, true);
        var window = new Window { Content = tb };
        window.Show();
        tb.Focus();
        return tb;
    }

    private static void Type(TextBox tb, string text, int caret)
    {
        tb.Text = text;
        tb.CaretIndex = caret;
    }

    [AvaloniaFact]
    public void TypingOpenBracket_ShowsPopupWithCandidates()
    {
        var tb = Setup();
        Type(tb, "[Pla", 4);
        var popup = TokenCompletion.GetPopup(tb)!;
        Assert.True(popup.IsOpen);
        Assert.True(((ListBox)popup.Child!).ItemCount > 0);
    }

    [AvaloniaFact]
    public void Enter_InsertsSelectedCompletion_AndClosesPopup()
    {
        var tb = Setup();
        Type(tb, "[Player Nam", 11);
        var popup = TokenCompletion.GetPopup(tb)!;
        ((ListBox)popup.Child!).SelectedIndex = 0;
        tb.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Contains("[Player Name]", tb.Text);
        Assert.False(popup.IsOpen);
    }

    [AvaloniaFact]
    public void Escape_HidesPopup()
    {
        var tb = Setup();
        Type(tb, "[Pla", 4);
        var popup = TokenCompletion.GetPopup(tb)!;
        Assert.True(popup.IsOpen);
        tb.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.False(popup.IsOpen);
    }
}
