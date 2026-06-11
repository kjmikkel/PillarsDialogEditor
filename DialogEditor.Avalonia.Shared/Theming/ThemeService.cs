using CommunityToolkit.Mvvm.ComponentModel;

namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// A process-wide "the theme just changed" ticker. <see cref="Revision"/> is bumped on every
/// palette swap and is bound (as a throwaway extra source) into the converter-driven
/// MultiBindings on the canvas. Those converters compute their brush key from data, so they
/// can't use <c>DynamicResource</c>; feeding them a changing value is the idiomatic way to
/// force the binding — and therefore the converter — to re-run, so node tints retint live.
/// Singleton because XAML binds <c>{x:Static}</c> to it and there is exactly one running app.
/// </summary>
public sealed partial class ThemeService : ObservableObject
{
    public static ThemeService Current { get; } = new();

    private ThemeService() { }

    [ObservableProperty] private int _revision;

    internal void Bump() => Revision++;
}
