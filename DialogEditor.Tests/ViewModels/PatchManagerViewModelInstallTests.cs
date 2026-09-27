using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class PatchManagerViewModelInstallTests : IDisposable
{
    private readonly FakePoe2Game _game = new("en", "fr");

    public PatchManagerViewModelInstallTests() => Loc.Configure(new StubStringProvider());
    public void Dispose() => _game.Dispose();

    private PatchManagerViewModel MakeVm(params DialogProject[] mods)
    {
        var vm = new PatchManagerViewModel(new StubFolderPicker(), new StubFilePicker())
        {
            GameFolder = _game.Root,
        };
        foreach (var m in mods) vm.Entries.Add(new PatchEntryViewModel(m.Name, m));
        return vm;
    }

    [Fact]
    public async Task Apply_Twice_Succeeds_AndEnablesRemoveAll()
    {
        var vm = MakeVm(FakePoe2Game.ExternalVoMod("A", "a"));
        Assert.False(vm.RemoveAllModsCommand.CanExecute(null));

        await vm.ApplyCommand.ExecuteAsync(null);
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("a", _game.ReadExternalVo());
        Assert.True(vm.HasInstalledMods);
        Assert.True(vm.RemoveAllModsCommand.CanExecute(null));
        Assert.StartsWith("PatchManager_ApplySuccess", vm.StatusText);
    }

    [Fact]
    public async Task Apply_WritesTranslations()
    {
        var patch = new ConversationPatch("test_conv", ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<DialogEditor.Core.Models.NodeTranslation>>
            {
                ["fr"] = [new(1, "modded fr", "")],
            },
        };
        var vm = MakeVm(DialogProject.Empty("T").WithPatch(patch));

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Contains("modded fr", File.ReadAllText(_game.StPath("fr")));
    }

    [Fact]
    public async Task RemoveAll_Confirmed_RestoresOriginals()
    {
        var before = _game.SnapshotGameData();
        var vm = MakeVm(FakePoe2Game.ExternalVoMod("A", "a"));
        vm.ConfirmRemoveAllMods = () => Task.FromResult(true);
        await vm.ApplyCommand.ExecuteAsync(null);

        await vm.RemoveAllModsCommand.ExecuteAsync(null);

        Assert.Equal(before, _game.SnapshotGameData());
        Assert.False(vm.HasInstalledMods);
        Assert.Single(vm.Entries);   // the load order is kept
    }

    [Fact]
    public async Task RemoveAll_Declined_ChangesNothing()
    {
        var vm = MakeVm(FakePoe2Game.ExternalVoMod("A", "a"));
        vm.ConfirmRemoveAllMods = () => Task.FromResult(false);
        await vm.ApplyCommand.ExecuteAsync(null);

        await vm.RemoveAllModsCommand.ExecuteAsync(null);

        Assert.Equal("a", _game.ReadExternalVo());
    }

    [Theory]
    [InlineData(true,  "b")]
    [InlineData(false, "a")]
    public async Task Apply_ExternalChanges_AsksAndHonoursAnswer(bool accept, string expectedVo)
    {
        var vm = MakeVm(FakePoe2Game.ExternalVoMod("A", "a"));
        await vm.ApplyCommand.ExecuteAsync(null);
        // Change a managed file externally: the bundle's ".bak" sidecar (Created by the first
        // apply) — the bundle itself stays ours, so ExternalVO remains readable.
        File.WriteAllText(_game.ConvPath() + ".bak", "someone else");
        IReadOnlyList<string>? asked = null;
        vm.ConfirmAcceptExternalChanges = paths => { asked = paths; return Task.FromResult(accept); };
        vm.Entries.Clear();
        vm.Entries.Add(new PatchEntryViewModel("B", FakePoe2Game.ExternalVoMod("B", "b")));

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.NotNull(asked);
        Assert.Equal(expectedVo, _game.ReadExternalVo());
        if (!accept) Assert.Equal("PatchManager_ApplyCancelledExternal", vm.StatusText);
    }

    [Fact]
    public void GameFolder_WithBackup_ShowsBackupStatus()
    {
        var vm = MakeVm();
        Assert.Equal("PatchManager_NoModsInstalled", vm.BackupStatusText);
    }
}
