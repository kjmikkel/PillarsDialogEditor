using CommunityToolkit.Mvvm.ComponentModel;

namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// Process-wide "the font scale just changed" ticker. <see cref="Revision"/> is bumped on
/// every call to <see cref="FontScaleApplier.Apply"/>. Bindings that can't use
/// <c>DynamicResource</c> (e.g. canvas <c>MultiBinding</c> converters that compute sizes
/// from data) subscribe to <see cref="PropertyChanged"/> and re-evaluate via
/// <see cref="ThemeService"/> — the same pattern as <see cref="ThemeService"/>.
/// Singleton because XAML binds <c>{x:Static}</c> to it and there is exactly one running app.
/// </summary>
public sealed partial class FontScaleService : ObservableObject
{
    public static FontScaleService Current { get; } = new();

    private FontScaleService() { }

    [ObservableProperty] private int _revision;

    internal void Bump() => Revision++;
}
