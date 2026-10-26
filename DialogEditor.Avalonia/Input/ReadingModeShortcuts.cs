using Avalonia.Input;

namespace DialogEditor.Avalonia.Input;

/// The only main-window and canvas shortcuts that still act in reading mode (issue 234): canvas
/// navigation, pane cycling, and opening/closing/settings. Callers `return` early for every other
/// key without marking it handled, so text boxes still receive the keys they need (Delete, Ctrl+Z).
public static class ReadingModeShortcuts
{
    public static bool IsAllowed(Key key, KeyModifiers modifiers) => (key, modifiers) switch
    {
        (Key.Left or Key.Right or Key.Up or Key.Down, KeyModifiers.None) => true,
        (Key.PageUp or Key.PageDown or Key.Home, KeyModifiers.None)      => true,
        (Key.Enter or Key.Escape or Key.Apps, KeyModifiers.None)         => true,
        (Key.F6, KeyModifiers.None or KeyModifiers.Shift)                => true,
        (Key.F10, KeyModifiers.Shift)                                    => true,
        (Key.O, KeyModifiers.Control)                                    => true,
        (Key.O, KeyModifiers.Control | KeyModifiers.Shift)               => true,
        (Key.W, KeyModifiers.Control)                                    => true,
        (Key.OemComma, KeyModifiers.Control)                             => true,
        _                                                                => false,
    };
}
