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
}
