using System.Linq;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class TextTagValidationViewModelStaleTests
{
    public TextTagValidationViewModelStaleTests() => Loc.Configure(new StubStringProvider());

    private static StaleDataRow Confirmed(int id, StaleDataKind kind = StaleDataKind.Comment, string? lang = null)
        => new("conv_a", id, kind, lang, StaleConfidence.Confirmed);
    private static StaleDataRow Likely(int id)
        => new("conv_a", id, StaleDataKind.Comment, null, StaleConfidence.Likely);

    [Fact]
    public void StaleRows_PopulateFromScan()
    {
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            staleScan: _ => [Confirmed(7), Likely(8)],
            canCheckGameFiles: true);

        Assert.Equal(2, vm.StaleRows.Count);
        Assert.True(vm.HasStaleData);
    }

    [Fact]
    public void CleanUpStale_PrunesConfirmedRowsOnly()
    {
        IReadOnlyList<StaleDataRow>? pruned = null;
        var scanQueue = new Queue<IReadOnlyList<StaleDataRow>>(
        [
            [Confirmed(7), Likely(8)],  // initial
            [Likely(8)],                // after prune re-scan
        ]);

        var vm = new TextTagValidationViewModel(
            scan: () => [],
            staleScan: _ => scanQueue.Dequeue(),
            prune: rows => pruned = rows,
            canCheckGameFiles: true);

        vm.CleanUpStaleCommand.Execute(null);       // arm
        vm.ConfirmCleanUpStaleCommand.Execute(null); // confirm

        Assert.NotNull(pruned);
        Assert.All(pruned!, r => Assert.Equal(StaleConfidence.Confirmed, r.Confidence));
        Assert.Single(pruned!);                      // only the one confirmed row
        Assert.Single(vm.StaleRows);                 // re-scan shows the remaining likely row
    }

    [Fact]
    public void LikelyRow_RemoveCommand_PrunesJustThatRow()
    {
        IReadOnlyList<StaleDataRow>? pruned = null;
        var scanQueue = new Queue<IReadOnlyList<StaleDataRow>>([ [Likely(8)], [] ]);

        var vm = new TextTagValidationViewModel(
            scan: () => [],
            staleScan: _ => scanQueue.Dequeue(),
            prune: rows => pruned = rows,
            canCheckGameFiles: true);

        var likelyVm = vm.StaleRows.Single();
        Assert.True(likelyVm.CanRemove);
        likelyVm.RemoveCommand.Execute(null);

        Assert.Equal(8, Assert.Single(pruned!).NodeId);
        Assert.Empty(vm.StaleRows);
    }

    [Fact]
    public void RemoveCommand_GuardsConfirmedRows()
    {
        var confirmedCalled = false;
        var confirmedVm = new StaleDataRowViewModel(Confirmed(7), "en", _ => confirmedCalled = true);
        Assert.False(confirmedVm.RemoveCommand.CanExecute(null));
        confirmedVm.RemoveCommand.Execute(null);
        Assert.False(confirmedCalled);

        var likelyVm = new StaleDataRowViewModel(Likely(8), "en", _ => { });
        Assert.True(likelyVm.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public void CheckGameFiles_Toggle_PassesFlagToStaleScan()
    {
        bool? lastFlag = null;
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            staleScan: includeLikely => { lastFlag = includeLikely; return []; },
            canCheckGameFiles: true);

        Assert.False(lastFlag);      // initial scan defaults to confirmed-only
        vm.CheckGameFiles = true;
        Assert.True(lastFlag);       // toggling re-scans with likely enabled
    }
}
