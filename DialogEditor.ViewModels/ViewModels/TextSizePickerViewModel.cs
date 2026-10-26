using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// <summary>One row in the text-size ComboBox: the scale plus its localised label ("150%").</summary>
public sealed record TextSizeChoice(double Scale, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// The Patcher's text-size picker (GitHub issue 263). The Patcher is the Player's only app, so
/// it has its own setting rather than following the editor's: <see cref="AppSettings.PatcherFontScale"/>.
/// The steps match the editor's font scale, and a pick applies at once.
/// </summary>
public partial class TextSizePickerViewModel : ObservableObject
{
    private static readonly double[] Scales = [1.0, 1.25, 1.5, 1.75, 2.0];

    private readonly IFontScaleApplier _applier;

    public IReadOnlyList<TextSizeChoice> Options { get; }

    [ObservableProperty] private TextSizeChoice _selected;

    public TextSizePickerViewModel(IFontScaleApplier applier)
    {
        _applier = applier;
        Options = Scales
            .Select(s => new TextSizeChoice(s, Loc.Format("Settings_TextSizePercent", Math.Round(s * 100))))
            .ToList();
        // A hand-edited or retired value falls back to 100%.
        var saved = AppSettings.PatcherFontScale;
        _selected = Options.FirstOrDefault(o => Math.Abs(o.Scale - saved) < 0.001) ?? Options[0];
    }

    partial void OnSelectedChanged(TextSizeChoice value)
    {
        if (value is null) return;
        AppSettings.PatcherFontScale = value.Scale;
        _applier.Apply(value.Scale);
    }
}
