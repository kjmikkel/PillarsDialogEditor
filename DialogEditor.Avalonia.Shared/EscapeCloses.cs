using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DialogEditor.Avalonia.Shared;

/// <summary>
/// Closes a tool window on Esc, doing what its close box does (GitHub issue 260). Set
/// <c>shared:EscapeCloses.IsEnabled="True"</c> on the Window.
///
/// Only for windows with no Cancel choice of their own. A dialog puts <c>IsCancel="True"</c>
/// on its safe button instead, so Esc runs that button's handler and sets the dialog's result;
/// using both would race, and EscapeRouteTests rejects it. A key some control already handled
/// (an open dropdown or completion popup closing itself) is left alone, so the first Esc
/// closes the popup and only a second one closes the window.
/// </summary>
public static class EscapeCloses
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Window, Window, bool>("IsEnabled");

    static EscapeCloses()
    {
        IsEnabledProperty.Changed.AddClassHandler<Window>((window, e) =>
        {
            window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            if (e.NewValue is true)
                window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        });
    }

    public static bool GetIsEnabled(Window window) => window.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Window window, bool value) => window.SetValue(IsEnabledProperty, value);

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || sender is not Window window)
            return;
        e.Handled = true;
        window.Close();
    }
}
