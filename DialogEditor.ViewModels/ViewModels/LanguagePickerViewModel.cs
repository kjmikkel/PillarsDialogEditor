using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Resources;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// <summary>One row in the language ComboBox.</summary>
public sealed record LanguageChoice(string Id, string DisplayName);

/// <summary>
/// Drives the shared language picker. On selection change it persists to
/// <see cref="AppSettings.UiLanguage"/>, applies the overlay live via the injected
/// <see cref="ILanguageApplier"/>, and updates <see cref="CoreLocale"/> so the four
/// Core-layer strings also retranslate.
/// </summary>
public partial class LanguagePickerViewModel : ObservableObject
{
    private readonly ILanguageApplier _applier;

    public IReadOnlyList<LanguageChoice> AvailableLanguages { get; }

    [ObservableProperty] private LanguageChoice _selectedLanguage;

    public LanguagePickerViewModel(ILanguageApplier applier)
    {
        _applier = applier;
        AvailableLanguages = applier.Available
            .Select(o => new LanguageChoice(o.Id, Loc.Get(o.DisplayNameKey)))
            .ToList();

        var savedId = AppSettings.UiLanguage;
        _selectedLanguage = AvailableLanguages.FirstOrDefault(c => c.Id == savedId)
                            ?? AvailableLanguages[0];
    }

    partial void OnSelectedLanguageChanged(LanguageChoice value)
    {
        if (value is null) return;
        AppSettings.UiLanguage = value.Id;
        _applier.Apply(value.Id);
        CoreLocale.SetCulture(value.Id);
    }
}
