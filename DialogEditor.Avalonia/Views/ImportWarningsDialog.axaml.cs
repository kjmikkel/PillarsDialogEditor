using Avalonia.Controls;
using Avalonia.Input;
using DialogEditor.Core.Import;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Views;

public partial class ImportWarningsDialog : Window
{
    // Parameterless constructor required so the Avalonia XAML compiler can complete
    // first-pass type analysis (otherwise AVLN3000 blocks App.axaml compilation).
    public ImportWarningsDialog() => InitializeComponent();

    public ImportWarningsDialog(IReadOnlyList<ImportWarning> warnings)
    {
        InitializeComponent();

        WarningsList.ItemsSource = warnings
            .Select(w => $"<<{w.Construct}>> — {Loc.FormatCount("ImportWarnings_OccurrenceCount", w.Count)}")
            .ToList();

        OkButton.Click += (_, _) => Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Escape or Key.Enter)
        {
            e.Handled = true;
            Close();
        }
    }
}
