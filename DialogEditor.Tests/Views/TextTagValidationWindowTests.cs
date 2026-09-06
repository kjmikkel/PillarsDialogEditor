using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Views;

public class TextTagValidationWindowTests
{
    public TextTagValidationWindowTests() => Loc.Configure(new StubStringProvider());

    [AvaloniaFact]
    public void Constructs_WithIssues()
    {
        var vm = new TextTagValidationViewModel(() =>
            [new TextTagIssueRow("conv_a", 5, "fr", "msg")]);
        var window = new TextTagValidationWindow(vm);
        window.Show();
        Assert.True(window.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Constructs_WhenEmpty()
    {
        var window = new TextTagValidationWindow(new TextTagValidationViewModel(() => []));
        window.Show();
        Assert.True(window.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Window_BuildsAndBindsStaleRow()
    {
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            staleScan: _ => [new StaleDataRow("conv_a", 7, StaleDataKind.Comment, null, StaleConfidence.Confirmed)],
            canCheckGameFiles: true);

        var window = new TextTagValidationWindow(vm);
        window.Show();

        Assert.True(vm.HasStaleData);
        Assert.Single(vm.StaleRows);
        window.Close();
    }

    /// The threshold picker is the only way to reach a non-default similarity bar from
    /// the UI, so pin that it is actually bound both ways — a broken binding would leave
    /// a control that looks right and does nothing.
    [AvaloniaFact]
    public void Window_BindsThresholdPicker_AndSelectionReScans()
    {
        var seen = new List<double>();
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            dupScan: t => { seen.Add(t); return new DuplicateLineReport([], []); });

        var window = new TextTagValidationWindow(vm);
        window.Show();

        var combo = window.FindControl<ComboBox>("NearThresholdComboBox");
        Assert.NotNull(combo);
        Assert.Equal(DuplicateLineScanner.DefaultNearThreshold, combo!.SelectedItem);

        seen.Clear();
        combo.SelectedItem = 0.75;

        Assert.Equal(0.75, vm.NearThreshold);
        Assert.Equal(0.75, Assert.Single(seen));
        window.Close();
    }
}
