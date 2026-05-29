using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Patch;
using DialogEditor.Patch.GitConflict;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

public class GitConflictResolutionWindowTests
{
    public GitConflictResolutionWindowTests() => Loc.Configure(new StubStringProvider());

    private static GitConflictResolutionViewModel MakeVm()
    {
        static DialogProject P(string to)
        {
            var mod = new NodeModification(
                4, new Dictionary<string, FieldChange> { ["DefaultText"] = new FieldChange("orig", to) }, [], []);
            return DialogProject.Empty("p").WithPatch(
                new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [], [mod]));
        }

        var mine   = P("friend");
        var theirs = P("traveler");
        return new GitConflictResolutionViewModel(mine, theirs, GitMergeAnalyzer.Analyze(mine, theirs));
    }

    [AvaloniaFact]
    public void ConflictList_ShowsOneItem()
    {
        var vm     = MakeVm();
        var window = new GitConflictResolutionWindow(vm);
        window.Show();

        Assert.Equal(1, window.FindControl<ListBox>("ConflictList")!.ItemCount);
    }

    [AvaloniaFact]
    public void ApplyButton_DisabledUntilResolved()
    {
        var vm     = MakeVm();
        var window = new GitConflictResolutionWindow(vm);
        window.Show();

        Assert.False(window.FindControl<Button>("ApplyButton")!.Command!.CanExecute(null));
    }

    [AvaloniaFact]
    public void ResolveAndApply_ProducesMergedResult()
    {
        var vm     = MakeVm();
        var window = new GitConflictResolutionWindow(vm);
        window.Show();

        vm.Conflicts[0].Choice = MergeSide.Theirs;

        var apply = window.FindControl<Button>("ApplyButton")!;
        Assert.True(apply.Command!.CanExecute(null));
        apply.Command!.Execute(null);

        Assert.NotNull(vm.Result);
        var mod = vm.Result!.Patches["greeting"].ModifiedNodes.Single(m => m.NodeId == 4);
        Assert.Equal("traveler", mod.FieldChanges["DefaultText"].To);
    }
}
