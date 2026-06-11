namespace DialogEditor.ViewModels.Services;

/// <summary>One selectable theme/palette: its persisted <paramref name="Id"/> and the
/// localisation key for its display name. Sourced from the Avalonia layer's palette
/// catalogue so the ViewModel never hardcodes "there are four palettes" (Layer 1 §8).</summary>
public sealed record ThemeOption(string Id, string DisplayNameKey);

/// <summary>
/// Framework-agnostic seam for runtime palette switching (Layer 2). The Avalonia
/// implementation swaps the merged palette + token dictionaries and retints the live app;
/// tests inject a stub. Mirrors the <c>IFolderPicker</c> injection that keeps the
/// settings ViewModels testable without a UI.
/// </summary>
public interface IThemeApplier
{
    /// <summary>The palettes the user may choose between, in display order (default first).</summary>
    IReadOnlyList<ThemeOption> Available { get; }

    /// <summary>Apply the palette with the given <paramref name="id"/> to the running app.</summary>
    void Apply(string id);
}
