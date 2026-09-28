using System.IO.Compression;
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

    // ── AddEntries ────────────────────────────────────────────────────────

    [Fact]
    public async Task AddEntries_PickerShowsPacksAndProjects()
    {
        // Players install .dialogpack files; a filter matching only *.dialogproject showed
        // them an empty folder until they switched to "All files" (issue #80).
        var picker = new StubFilePicker();
        var vm     = new PatchManagerViewModel(new StubFolderPicker(), picker);

        await vm.AddEntriesCommand.ExecuteAsync(null);

        Assert.Equal([".dialogpack", ".dialogproject"], picker.OpenFilesExtensions);
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
    public void Analyse_RaisesConflictsChanged_SoTheViewShowsTheRows()
    {
        // The conflict list is bound to Conflicts. Without a change notification the view
        // kept the empty list it read at startup and showed only the "N conflicts" summary,
        // never which conversation or mods were involved (issue #80).
        var mod = new Dictionary<string, FieldChange> { ["DefaultText"] = new("\"a\"", "\"b\"") };
        ConversationPatch Patch() => new("conv1", ConversationPatch.CurrentSchemaVersion, [], [],
            [new NodeModification(5, mod, [], [])]);

        var vm      = MakeVm();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Entries.Add(new PatchEntryViewModel("mod1.dialogproject", DialogProject.Empty("Mod1").WithPatch(Patch())));
        vm.Entries.Add(new PatchEntryViewModel("mod2.dialogproject", DialogProject.Empty("Mod2").WithPatch(Patch())));

        Assert.NotEmpty(vm.Conflicts);
        Assert.Contains(nameof(PatchManagerViewModel.Conflicts), changed);
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

    // ── Save / Load load order ────────────────────────────────────────────

    /// A .patchlist stores the .dialogpack path itself (FullPath), so reloading it
    /// must extract the pack again rather than JSON-parse the zip — otherwise the
    /// entry shows as "could not load" and its vo/ folder is lost.
    [Fact]
    public async Task LoadFromFile_ReloadsDialogPackEntry_WithVoFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"PMTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var entryTempDirs = new List<string?>();
        try
        {
            var packPath = Path.Combine(dir, "mod.dialogpack");
            using (var archive = ZipFile.Open(packPath, ZipArchiveMode.Create))
            {
                var projectEntry = archive.CreateEntry("project.dialogproject");
                using (var sw = new StreamWriter(projectEntry.Open()))
                    sw.Write(DialogProjectSerializer.Serialize(DialogProject.Empty("PackMod")));
                var voEntry = archive.CreateEntry("vo/line.wem");
                using (var sw = new StreamWriter(voEntry.Open()))
                    sw.Write("fakewem");
            }
            var patchlistPath = Path.Combine(dir, "order.patchlist");

            var saving = new PatchManagerViewModel(new StubFolderPicker(),
                new StubFilePicker(saveResult: patchlistPath, multiResult: [packPath]));
            await saving.AddEntriesCommand.ExecuteAsync(null);
            entryTempDirs.AddRange(saving.Entries.Select(e => e.TempDir));
            await saving.SaveLoadOrderCommand.ExecuteAsync(null);

            var loading = MakeVm();
            loading.LoadFromFile(patchlistPath);
            entryTempDirs.AddRange(loading.Entries.Select(e => e.TempDir));

            var entry = Assert.Single(loading.Entries);
            Assert.True(entry.IsLoaded, $"Expected the pack to load, got: {entry.LoadError}");
            Assert.Equal("PackMod", entry.ProjectName);
            Assert.Equal(Path.GetFullPath(packPath), Path.GetFullPath(entry.FullPath));
            Assert.NotNull(entry.TempDir);
            Assert.NotNull(entry.VoFolder);
            Assert.True(File.Exists(Path.Combine(entry.VoFolder!, "line.wem")));
        }
        finally
        {
            foreach (var d in entryTempDirs.Append(dir).OfType<string>())
            {
                try { Directory.Delete(d, recursive: true); }
                catch { /* best-effort cleanup */ }
            }
        }
    }
}
