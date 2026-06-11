using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// <summary>One row in the theme ComboBox: the persisted <see cref="Id"/> plus the
/// already-localised <see cref="DisplayName"/> the user sees.</summary>
public sealed record ThemeChoice(string Id, string DisplayName);

/// <summary>
/// Drives the shared theme picker (Layer 2). Holds the choice as a <see cref="ThemeChoice"/>
/// bound to a ComboBox; on change it persists to <see cref="AppSettings.Theme"/> and asks the
/// injected <see cref="IThemeApplier"/> to retint the live app — mirroring
/// <see cref="SettingsViewModel.OnLocalizationFormatChanged"/>. The catalogue comes from the
/// applier (the Avalonia layer), so this VM never hardcodes how many palettes exist.
/// </summary>
public partial class ThemePickerViewModel : ObservableObject
{
    private readonly IThemeApplier _applier;

    public IReadOnlyList<ThemeChoice> AvailableThemes { get; }

    [ObservableProperty] private ThemeChoice _selectedTheme;

    public ThemePickerViewModel(IThemeApplier applier)
    {
        _applier = applier;
        AvailableThemes = applier.Available
            .Select(o => new ThemeChoice(o.Id, Loc.Get(o.DisplayNameKey)))
            .ToList();

        // Restore the persisted choice; fall back to the first (default) palette if the
        // saved id is unknown (e.g. a palette was removed since it was chosen).
        var savedId = AppSettings.Theme;
        _selectedTheme = AvailableThemes.FirstOrDefault(c => c.Id == savedId)
                         ?? AvailableThemes[0];
    }

    partial void OnSelectedThemeChanged(ThemeChoice value)
    {
        if (value is null) return;
        AppSettings.Theme = value.Id;
        _applier.Apply(value.Id);
    }
}
