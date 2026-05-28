using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using DialogEditor.Core.Import;

namespace DialogEditor.Avalonia.Views;

public partial class ImportWarningsDialog : Window
{
    // Parameterless constructor required so the Avalonia XAML compiler can complete
    // first-pass type analysis (otherwise AVLN3000 blocks App.axaml compilation).
    public ImportWarningsDialog() => InitializeComponent();

    public ImportWarningsDialog(IReadOnlyList<ImportWarning> warnings)
    {
        InitializeComponent();

        var suffix = Application.Current!.FindResource("ImportWarnings_OccurrenceSuffix") as string
                     ?? "occurrence(s)";
        WarningsList.ItemsSource = warnings
            .Select(w => $"<<{w.Construct}>> — {w.Count} {suffix}")
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
