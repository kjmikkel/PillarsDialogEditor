using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DialogEditor.Avalonia.Shared;
using Xunit;

namespace DialogEditor.Tests.Controls;

/// <summary>
/// Gaps.md a11y item 13: FocusHintBar mirrors the focused control's
/// AutomationProperties.HelpText into its own Text, the same way
/// MainWindow.OnAnyGotFocus feeds MainWindowViewModel.FocusHintText (item 5 Part B),
/// but as a self-contained control any secondary window can drop in.
/// </summary>
public class FocusHintBarTests
{
    [AvaloniaFact]
    public void GotFocus_OnElementWithHelpText_SetsText()
    {
        var window = new Window();
        var bar = new FocusHintBar();
        var button = new Button();
        AutomationProperties.SetHelpText(button, "Does the thing");

        var root = new StackPanel();
        root.Children.Add(button);
        root.Children.Add(bar);
        window.Content = root;

        bar.AttachTo(window);
        window.Show();

        button.RaiseEvent(new GotFocusEventArgs
        {
            RoutedEvent = InputElement.GotFocusEvent,
            NavigationMethod = NavigationMethod.Tab,
        });

        Assert.Equal("Does the thing", bar.Text);
    }

    [AvaloniaFact]
    public void GotFocus_OnElementWithoutHelpText_ClearsText()
    {
        var window = new Window();
        var bar = new FocusHintBar();
        var button = new Button();

        var root = new StackPanel();
        root.Children.Add(button);
        root.Children.Add(bar);
        window.Content = root;

        bar.AttachTo(window);
        window.Show();
        bar.Text = "stale hint";

        button.RaiseEvent(new GotFocusEventArgs
        {
            RoutedEvent = InputElement.GotFocusEvent,
            NavigationMethod = NavigationMethod.Tab,
        });

        Assert.Equal(string.Empty, bar.Text);
    }
}
