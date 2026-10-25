using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared;

namespace DialogEditor.Tests.Controls;

/// <summary>
/// GitHub issue 254: five tool windows showed a blank list with no reason. EmptyState is the
/// one control they share: a message saying why the list is empty and a hint saying what
/// fills it. It shows itself only while it has a message, so a window binds it to its view
/// model and never toggles visibility by hand.
/// </summary>
public class EmptyStateTests
{
    private static (Window, EmptyState) Show(string message, string hint)
    {
        var state = new EmptyState { Message = message, Hint = hint };
        var window = new Window { Content = state };
        window.Show();
        return (window, state);
    }

    private static TextBlock Text(EmptyState state, string name) =>
        state.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == name);

    [AvaloniaFact]
    public void NoMessage_IsHidden()
    {
        var (window, state) = Show("", "a hint");
        Assert.False(state.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Message_ShowsMessageAndHint()
    {
        var (window, state) = Show("Nothing here yet", "Press Find");
        Assert.True(state.IsVisible);
        Assert.Equal("Nothing here yet", Text(state, "MessageText").Text);
        Assert.Equal("Press Find", Text(state, "HintText").Text);
        Assert.True(Text(state, "HintText").IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void NoHint_HidesTheHintLine()
    {
        var (window, state) = Show("Nothing here yet", "");
        Assert.False(Text(state, "HintText").IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void MessageChange_TogglesVisibility()
    {
        var (window, state) = Show("", "");
        state.Message = "Now empty";
        Assert.True(state.IsVisible);
        state.Message = "";
        Assert.False(state.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void ScreenReaderName_IsTheMessageAndHint()
    {
        var (window, state) = Show("No matches", "Try fewer words");
        Assert.Equal("No matches. Try fewer words", AutomationProperties.GetName(state));
        window.Close();
    }
}
