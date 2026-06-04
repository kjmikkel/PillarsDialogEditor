using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.GitConflict;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class GitConflictResolutionViewModelTests
{
    public GitConflictResolutionViewModelTests() => Loc.Configure(new StubStringProvider());

    private static (DialogProject Mine, DialogProject Theirs, IReadOnlyList<MergeConflict> Conflicts) FieldEditCase()
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
        return (mine, theirs, GitMergeAnalyzer.Analyze(mine, theirs));
    }

    private static (DialogProject Mine, DialogProject Theirs, IReadOnlyList<MergeConflict> Conflicts) TranslationCase(
        string mineDefault, string theirsDefault, string mineFemale, string theirsFemale)
    {
        DialogProject P(string def, string fem)
        {
            var patch = new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [], [])
            {
                Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                {
                    ["en"] = [new NodeTranslation(4, def, fem)],
                },
            };
            return DialogProject.Empty("p").WithPatch(patch);
        }

        var mine   = P(mineDefault, mineFemale);
        var theirs = P(theirsDefault, theirsFemale);
        return (mine, theirs, GitMergeAnalyzer.Analyze(mine, theirs));
    }

    [Fact]
    public void TranslationRow_WithFemaleText_HasFemaleRowAndValues()
    {
        var (m, t, c) = TranslationCase("Hello", "Hello", "HelloF", "HelloFemale");
        var vm = new GitConflictResolutionViewModel(m, t, c);
        var row = vm.Conflicts[0];

        Assert.True(row.HasFemaleRow);
        Assert.Equal("HelloF",      row.MineFemaleValue);
        Assert.Equal("HelloFemale", row.TheirsFemaleValue);
    }

    [Fact]
    public void TranslationRow_NoFemaleText_NoFemaleRow()
    {
        var (m, t, c) = TranslationCase("Hello friend", "Hello traveler", "", "");
        var vm = new GitConflictResolutionViewModel(m, t, c);

        Assert.False(vm.Conflicts[0].HasFemaleRow);
    }

    [Fact]
    public void FieldEditRow_HasNoFemaleRow()
    {
        var (m, t, c) = FieldEditCase();
        var vm = new GitConflictResolutionViewModel(m, t, c);

        Assert.False(vm.Conflicts[0].HasFemaleRow);
    }

    [Fact]
    public void NewVm_Unresolved_ApplyDisabled()
    {
        var (m, t, c) = FieldEditCase();
        var vm = new GitConflictResolutionViewModel(m, t, c);

        Assert.False(vm.AllResolved);
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public void ResolvingAll_EnablesApply()
    {
        var (m, t, c) = FieldEditCase();
        var vm = new GitConflictResolutionViewModel(m, t, c);

        vm.Conflicts[0].Choice = MergeSide.Theirs;

        Assert.True(vm.AllResolved);
        Assert.True(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public void Apply_BuildsMergedResult()
    {
        var (m, t, c) = FieldEditCase();
        var vm = new GitConflictResolutionViewModel(m, t, c);

        vm.Conflicts[0].Choice = MergeSide.Theirs;
        vm.ApplyCommand.Execute(null);

        Assert.NotNull(vm.Result);
        var mod = vm.Result!.Patches["greeting"].ModifiedNodes.Single(x => x.NodeId == 4);
        Assert.Equal("traveler", mod.FieldChanges["DefaultText"].To);
    }

    [Fact]
    public void Apply_RaisesRequestClose()
    {
        var (m, t, c) = FieldEditCase();
        var vm = new GitConflictResolutionViewModel(m, t, c);
        vm.Conflicts[0].Choice = MergeSide.Theirs;

        var closed = false;
        vm.RequestClose += () => closed = true;
        vm.ApplyCommand.Execute(null);

        Assert.True(closed);
    }

    [Fact]
    public void Selected_DefaultsToFirstConflict()
    {
        var (m, t, c) = FieldEditCase();
        var vm = new GitConflictResolutionViewModel(m, t, c);

        Assert.Same(vm.Conflicts[0], vm.Selected);
    }
}
