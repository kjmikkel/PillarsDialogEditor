using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DialogEditor.Avalonia.Shared;

public partial class FocusHintBar : UserControl
{
    public FocusHintBar() => InitializeComponent();

    public string Text
    {
        get => HintText.Text ?? string.Empty;
        set => HintText.Text = value;
    }

    /// <summary>
    /// Mirrors MainWindow.OnAnyGotFocus (item 5 Part B): on any focus change within
    /// <paramref name="window"/>, copy the focused element's HelpText into Text.
    /// </summary>
    public void AttachTo(Window window)
        => window.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble);

    private void OnGotFocus(object? sender, GotFocusEventArgs e)
        => Text = e.Source is StyledElement el
            ? AutomationProperties.GetHelpText(el) ?? string.Empty
            : string.Empty;
}
