using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Core.Analytics;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Avalonia.Headless.XUnit;

namespace DialogEditor.Tests.Localisation;

// End-to-end companion to FlowAnalyticsViewModelTests: those run against
// StubStringProvider, which echoes every key back, so a key that is missing or
// misspelled in Strings.axaml still passes there. These resolve the real
// dictionary through the real AvaloniaStringProvider, pinning both that the
// FlowAnalytics row keys exist and that their {0}/{1} placeholders are wired in
// the order the ViewModel passes its arguments.
public class FlowAnalyticsResourceEndToEndTests
{
    public FlowAnalyticsResourceEndToEndTests() => Loc.Configure(new AvaloniaStringProvider());

    [AvaloniaFact]
    public void FlowIssue_DisplayText_UsesRealResources()
    {
        var vm = new FlowIssueViewModel(
            new FlowIssue(7, FlowIssueKind.EmptyText), "Hello there", _ => { });

        Assert.Equal("Node 7 \u2014 Hello there", vm.DisplayText);
    }

    [AvaloniaFact]
    public void TokenIssueRow_DisplayText_UsesRealResources()
    {
        Assert.Equal("Node 7 (de): bad tag",
            new TokenIssueRowViewModel(7, "de", "bad tag", _ => { }).DisplayText);
        Assert.Equal("Node 7: bad tag",
            new TokenIssueRowViewModel(7, "", "bad tag", _ => { }).DisplayText);
    }
}
