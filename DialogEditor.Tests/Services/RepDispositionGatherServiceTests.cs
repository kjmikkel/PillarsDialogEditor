using DialogEditor.Core.Editing;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class RepDispositionGatherServiceTests
{
    private static FakeGameDataProvider Provider() => new("poe2", "en");

    [Fact]
    public void Gather_CurrentScope_ReturnsOnlyOpenConversation()
    {
        var open = new ConversationEditSnapshot([]);

        var result = RepDispositionGatherService.Gather(
            BalanceSource.ProjectChanges, BalanceScope.Current,
            DialogProject.Empty("p"), Provider(), openName: "open_conv", openSnapshot: open);

        Assert.Single(result);
        Assert.Equal("open_conv", result[0].Name);
        Assert.Same(open, result[0].Snapshot);
    }

    [Fact]
    public void Gather_CurrentScope_NoOpenConversation_ReturnsEmpty()
    {
        var result = RepDispositionGatherService.Gather(
            BalanceSource.OnDiskPlusChanges, BalanceScope.Current,
            DialogProject.Empty("p"), Provider(),
            openName: null, openSnapshot: null);

        Assert.Empty(result);
    }

    [Fact]
    public void Gather_AllScope_ProjectChanges_NoPatches_ReturnsEmpty()
    {
        var result = RepDispositionGatherService.Gather(
            BalanceSource.ProjectChanges, BalanceScope.All,
            DialogProject.Empty("p"), Provider(),
            openName: null, openSnapshot: null);

        Assert.Empty(result);   // an empty project has no patched conversations
    }
}
