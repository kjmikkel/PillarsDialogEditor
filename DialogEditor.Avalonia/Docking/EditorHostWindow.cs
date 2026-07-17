using Avalonia.Controls;
using Avalonia.Platform;
using Dock.Avalonia.Controls;

namespace DialogEditor.Avalonia.Docking;

/// <summary>
/// Floating dock window (produced when a tab/document is torn off into its own OS window).
/// Carries the app icon, satisfying the project's "every Window carries app.ico" rule — Dock's
/// stock <see cref="HostWindow"/> has none. It automatically inherits the app's live theme
/// (Palette/Tokens/DockTheme) because those are merged Application-level resource dictionaries
/// that apply to every top-level, not something this subclass needs to opt into.
/// </summary>
public class EditorHostWindow : HostWindow
{
    public EditorHostWindow()
    {
        Icon = new WindowIcon(AssetLoader.Open(
            new Uri("avares://DialogEditor.Avalonia/Assets/app.ico")));
    }
}
