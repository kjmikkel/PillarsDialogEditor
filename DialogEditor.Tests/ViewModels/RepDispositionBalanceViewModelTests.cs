using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class RepDispositionBalanceViewModelTests : IDisposable
{
    public RepDispositionBalanceViewModelTests() => Loc.Configure(new StubStringProvider());
    public void Dispose() => GameDataNameService.Clear();

    private static ConditionLeaf Disp(string a) =>
        new("Boolean DispositionEqual(Axis, Rank)", new[] { a, "2" }, false, "And");

    [Fact]
    public async Task Refresh_CurrentScope_PopulatesRowsFromOpenConversation()
    {
        GameDataNameService.Register("Disposition", new[]
        {
            new NamedEntry("Benevolent", "Benevolent"),
            new NamedEntry("Cruel", "Cruel"),
        });
        var node = new NodeEditSnapshot(0, false, SpeakerCategory.Npc, "", "", "", "", "", "", "", "", "", false, false,
            [], new ConditionNode[] { Disp("Benevolent") }, []);
        var open = new ConversationEditSnapshot(new[] { node });

        var vm = new RepDispositionBalanceViewModel(
            DialogProject.Empty("p"), new FakeGameDataProvider("poe2", "en"),
            () => ("open", open)) { Source = BalanceSource.ProjectChanges, Scope = BalanceScope.Current };

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains(vm.DispositionRows, r => r.DisplayValue == "Benevolent" && r.Count == 1);
        Assert.Contains(vm.DispositionRows, r => r.DisplayValue == "Cruel" && r.Count == 0);
        Assert.False(vm.IsBusy);
    }

    // ── Empty state (GitHub issue 254) ────────────────────────────────────

    [Fact]
    public void EmptyState_BeforeAnalysis_IsBlank()
    {
        var vm = new RepDispositionBalanceViewModel(
            DialogProject.Empty("p"), new FakeGameDataProvider("poe2", "en"), () => (null, null));
        Assert.Equal("", vm.DispositionEmptyMessage);
        Assert.Equal("", vm.ReputationEmptyMessage);
        Assert.Equal("", vm.EmptyHint);
    }

    [Fact]
    public async Task EmptyState_TableWithNoRows_SaysSo_AndHowToLookFurther()
    {
        GameDataNameService.Register("Disposition", new[] { new NamedEntry("Benevolent", "Benevolent") });
        var node = new NodeEditSnapshot(0, false, SpeakerCategory.Npc, "", "", "", "", "", "", "", "", "", false, false,
            [], new ConditionNode[] { Disp("Benevolent") }, []);
        var open = new ConversationEditSnapshot(new[] { node });
        var vm = new RepDispositionBalanceViewModel(
            DialogProject.Empty("p"), new FakeGameDataProvider("poe2", "en"),
            () => ("open", open)) { Source = BalanceSource.ProjectChanges, Scope = BalanceScope.Current };

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.DispositionRows);
        Assert.Empty(vm.ReputationRows);
        Assert.Equal("", vm.DispositionEmptyMessage);
        Assert.Equal("EmptyState_RepBalance_NoReputations", vm.ReputationEmptyMessage);
        Assert.Equal("EmptyState_RepBalance_Hint", vm.EmptyHint);
    }

    [Fact]
    public async Task EmptyState_WidestSearch_HasNoHintToLookFurther()
    {
        var vm = new RepDispositionBalanceViewModel(
            DialogProject.Empty("p"), new FakeGameDataProvider("poe2", "en"),
            () => (null, null)) { Source = BalanceSource.OnDiskPlusChanges, Scope = BalanceScope.All };

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("EmptyState_RepBalance_NoReputations", vm.ReputationEmptyMessage);
        Assert.Equal("", vm.EmptyHint);
    }
}
