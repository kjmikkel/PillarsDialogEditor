using DialogEditor.Core.Editing;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class PatchManagerViewModelTests
{
    public PatchManagerViewModelTests() => Loc.Configure(new StubStringProvider());

    private static PatchManagerViewModel MakeVm() =>
        new(new StubFolderPicker(), new StubFilePicker());

    private static PatchEntryViewModel MakeEntry(string name = "mod.dialogproject") =>
        new(name, DialogProject.Empty(name));

    // ── HasEntries ────────────────────────────────────────────────────────

    [Fact]
    public void HasEntries_FalseWhenEmpty()
    {
        var vm = MakeVm();
        Assert.False(vm.HasEntries);
    }

    [Fact]
    public void HasEntries_TrueAfterEntryAdded()
    {
        var vm = MakeVm();
        vm.Entries.Add(MakeEntry());
        Assert.True(vm.HasEntries);
    }

    // ── RemoveEntry ───────────────────────────────────────────────────────

    [Fact]
    public void RemoveEntry_RemovesFromEntries()
    {
        var vm    = MakeVm();
        var entry = MakeEntry();
        vm.Entries.Add(entry);
        vm.RemoveEntryCommand.Execute(entry);
        Assert.Empty(vm.Entries);
    }

    [Fact]
    public void RemoveEntry_WithNull_DoesNothing()
    {
        var vm = MakeVm();
        vm.Entries.Add(MakeEntry());
        vm.RemoveEntryCommand.Execute(null);
        Assert.Single(vm.Entries);
    }

    // ── MoveUp ────────────────────────────────────────────────────────────

    [Fact]
    public void MoveUp_MovesEntryEarlierInList()
    {
        var vm = MakeVm();
        var e1 = MakeEntry("first");
        var e2 = MakeEntry("second");
        vm.Entries.Add(e1);
        vm.Entries.Add(e2);
        vm.MoveUpCommand.Execute(e2);
        Assert.Equal(e2, vm.Entries[0]);
    }

    [Fact]
    public void MoveUp_FirstEntry_DoesNothing()
    {
        var vm = MakeVm();
        var e1 = MakeEntry("first");
        vm.Entries.Add(e1);
        vm.Entries.Add(MakeEntry("second"));
        vm.MoveUpCommand.Execute(e1);
        Assert.Equal(e1, vm.Entries[0]);
    }

    // ── MoveDown ──────────────────────────────────────────────────────────

    [Fact]
    public void MoveDown_MovesEntryLaterInList()
    {
        var vm = MakeVm();
        var e1 = MakeEntry("first");
        var e2 = MakeEntry("second");
        vm.Entries.Add(e1);
        vm.Entries.Add(e2);
        vm.MoveDownCommand.Execute(e1);
        Assert.Equal(e1, vm.Entries[1]);
    }

    [Fact]
    public void MoveDown_LastEntry_DoesNothing()
    {
        var vm = MakeVm();
        vm.Entries.Add(MakeEntry("first"));
        var e2 = MakeEntry("second");
        vm.Entries.Add(e2);
        vm.MoveDownCommand.Execute(e2);
        Assert.Equal(e2, vm.Entries[1]);
    }

    // ── CanApply ──────────────────────────────────────────────────────────

    [Fact]
    public void CanApply_FalseWhenGameFolderEmpty()
    {
        var vm = MakeVm();
        vm.Entries.Add(MakeEntry());
        vm.GameFolder = string.Empty;
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public void CanApply_FalseWhenNoEntries()
    {
        var vm = MakeVm();
        vm.GameFolder = @"C:\SomeFolder";
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public void CanApply_TrueWhenGameFolderSetAndEntriesPresent()
    {
        var vm = MakeVm();
        vm.Entries.Add(MakeEntry());
        vm.GameFolder = @"C:\SomeFolder";
        Assert.True(vm.ApplyCommand.CanExecute(null));
    }

    // ── Analyse — conflict detection ──────────────────────────────────────

    [Fact]
    public void Analyse_NoConflicts_HasConflictsFalse()
    {
        var vm = MakeVm();
        // Two projects each patching a different conversation
        var p1 = DialogProject.Empty("Mod1").WithPatch(
            new ConversationPatch("conv1", ConversationPatch.CurrentSchemaVersion, [], [], []));
        var p2 = DialogProject.Empty("Mod2").WithPatch(
            new ConversationPatch("conv2", ConversationPatch.CurrentSchemaVersion, [], [], []));
        vm.Entries.Add(new PatchEntryViewModel("mod1.dialogproject", p1));
        vm.Entries.Add(new PatchEntryViewModel("mod2.dialogproject", p2));
        Assert.False(vm.HasConflicts);
    }

    [Fact]
    public void Analyse_TwoPacksAddTheSameNode_FlagsBothAndNamesThePacks()
    {
        // Issue #6: an added-node id clash used to go unreported, and no conflict row said
        // which packs were involved — only the entry badges hinted at it.
        static NodeEditSnapshot Node(string text) =>
            new(500, false, DialogEditor.Core.Models.SpeakerCategory.Npc, "", "", text, "",
                "Conversation", "None", "", "", "", false, false, [], [], []);

        var vm = MakeVm();
        var p1 = DialogProject.Empty("Mod1").WithPatch(
            new ConversationPatch("conv1", ConversationPatch.CurrentSchemaVersion, [Node("a")], [], []));
        var p2 = DialogProject.Empty("Mod2").WithPatch(
            new ConversationPatch("conv1", ConversationPatch.CurrentSchemaVersion, [Node("b")], [], []));
        vm.Entries.Add(new PatchEntryViewModel("mod1.dialogproject", p1));
        vm.Entries.Add(new PatchEntryViewModel("mod2.dialogproject", p2));

        Assert.True(vm.HasConflicts);
        var row = Assert.Single(vm.Conflicts);
        Assert.Equal(PatchConflictKind.AddedNode, row.Conflict.Kind);
        Assert.Equal(("Mod1", "Mod2"), (row.FirstProjectName, row.SecondProjectName));
        Assert.All(vm.Entries, e => Assert.True(e.HasConflict));
    }

    [Fact]
    public void Analyse_SkipsUnloadedEntries_WhenNamingThePacks()
    {
        // Conflict indices count LOADED entries only (Analyse filters on IsLoaded), so an
        // entry that failed to load in front of the pair must not shift the names.
        static ConversationPatch Edit(string to) => new("conv1", ConversationPatch.CurrentSchemaVersion, [], [],
            [new NodeModification(5, new Dictionary<string, FieldChange> { ["DefaultText"] = new("x", to) }, [], [])]);

        var vm = MakeVm();
        vm.Entries.Add(new PatchEntryViewModel("broken.dialogproject", "File not found"));
        vm.Entries.Add(new PatchEntryViewModel("mod1.dialogproject", DialogProject.Empty("Mod1").WithPatch(Edit("a"))));
        vm.Entries.Add(new PatchEntryViewModel("mod2.dialogproject", DialogProject.Empty("Mod2").WithPatch(Edit("b"))));

        var row = Assert.Single(vm.Conflicts);
        Assert.Equal(("Mod1", "Mod2"), (row.FirstProjectName, row.SecondProjectName));
        Assert.False(vm.Entries[0].HasConflict);
        Assert.True(vm.Entries[1].HasConflict);
        Assert.True(vm.Entries[2].HasConflict);
    }
}
