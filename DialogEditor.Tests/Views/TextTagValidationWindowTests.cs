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
}
