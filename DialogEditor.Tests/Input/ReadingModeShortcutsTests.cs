using Avalonia.Input;
using DialogEditor.Avalonia.Input;

namespace DialogEditor.Tests.Input;

/// Issue 234: the shortcuts that still work in reading mode. Everything else is swallowed by
/// the callers' early return (never marked handled, so text boxes still get their keys).
public class ReadingModeShortcutsTests
{
    public static TheoryData<Key, KeyModifiers> Allowed => new()
    {
        { Key.Left, KeyModifiers.None }, { Key.Right, KeyModifiers.None },
        { Key.Up, KeyModifiers.None },   { Key.Down, KeyModifiers.None },
        { Key.PageUp, KeyModifiers.None }, { Key.PageDown, KeyModifiers.None },
        { Key.Home, KeyModifiers.None }, { Key.Enter, KeyModifiers.None },
        { Key.Escape, KeyModifiers.None }, { Key.Apps, KeyModifiers.None },
        { Key.F6, KeyModifiers.None },   { Key.F6, KeyModifiers.Shift },
        { Key.F10, KeyModifiers.Shift },
        { Key.O, KeyModifiers.Control | KeyModifiers.Shift },
        { Key.O, KeyModifiers.Control }, { Key.W, KeyModifiers.Control },
        { Key.OemComma, KeyModifiers.Control },
    };

    public static TheoryData<Key, KeyModifiers> Blocked => new()
    {
        { Key.N, KeyModifiers.Control }, { Key.S, KeyModifiers.Control },
        { Key.S, KeyModifiers.Control | KeyModifiers.Shift },
        { Key.Z, KeyModifiers.Control }, { Key.Y, KeyModifiers.Control },
        { Key.Delete, KeyModifiers.None },
        { Key.F5, KeyModifiers.None }, { Key.F5, KeyModifiers.Shift },
        { Key.F7, KeyModifiers.None },
        { Key.B, KeyModifiers.Control | KeyModifiers.Shift },
        { Key.F, KeyModifiers.Control }, { Key.H, KeyModifiers.Control },
        { Key.H, KeyModifiers.Control | KeyModifiers.Shift },
        { Key.F, KeyModifiers.Control | KeyModifiers.Shift },
        { Key.L, KeyModifiers.Control },
        { Key.Right, KeyModifiers.Control }, { Key.Down, KeyModifiers.Control | KeyModifiers.Shift },
    };

    [Theory, MemberData(nameof(Allowed))]
    public void Allowed_Shortcuts_AreAllowed(Key key, KeyModifiers mods)
        => Assert.True(ReadingModeShortcuts.IsAllowed(key, mods));

    [Theory, MemberData(nameof(Blocked))]
    public void Editing_Shortcuts_AreNotAllowed(Key key, KeyModifiers mods)
        => Assert.False(ReadingModeShortcuts.IsAllowed(key, mods));
}
