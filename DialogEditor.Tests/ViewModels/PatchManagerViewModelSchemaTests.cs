using System.IO.Compression;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class PatchManagerViewModelSchemaTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public PatchManagerViewModelSchemaTests() => Loc.Configure(new StubStringProvider());
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception) { /* best-effort */ } }

    private string Project(string name, int version = 1)
    {
        var path = Path.Combine(_dir, name + ".dialogproject");
        DialogProjectSerializer.SaveToFile(path, FakePoe2Game.ExternalVoMod(name, "x"));
        if (version != 1)
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"SchemaVersion\": 1", $"\"SchemaVersion\": {version}"));
        return path;
    }

    private string Pack(string projectPath)
    {
        var pack = Path.ChangeExtension(projectPath, ".dialogpack");
        using var zip = ZipFile.Open(pack, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(projectPath, "project.dialogproject");
        return pack;
    }

    private static string DialogPackTempRoot =>
        Path.Combine(Path.GetTempPath(), "PillarsDialogEditor", "dialogpack");

    private static int TempPackDirCount() =>
        Directory.Exists(DialogPackTempRoot) ? Directory.GetDirectories(DialogPackTempRoot).Length : 0;

    private static PatchManagerViewModel Vm(params string[] picked) =>
        new(new StubFolderPicker(), new StubFilePicker(multiResult: picked));

    [Fact]
    public async Task AddEntries_NewerProject_IsNotAdded_AndStatusExplains()
    {
        var vm = Vm(Project("Ok"), Project("Future", version: 99));

        await vm.AddEntriesCommand.ExecuteAsync(null);

        Assert.Equal(["Ok"], vm.Entries.Select(e => e.ProjectName));
        Assert.StartsWith("Schema_TooNew", vm.StatusText);
    }

    [Fact]
    public async Task AddEntries_NewerPack_IsNotAdded_AndItsTempDirIsDeleted()
    {
        var before = TempPackDirCount();
        var vm = Vm(Pack(Project("Future", version: 99)));

        await vm.AddEntriesCommand.ExecuteAsync(null);

        Assert.Empty(vm.Entries);
        Assert.Equal(before, TempPackDirCount());
    }

    [Fact]
    public void LoadFromFile_ListWithANewerEntry_IsRefusedWhole_KeepingTheCurrentOrder()
    {
        var vm = Vm();
        vm.GameFolder = "kept";
        vm.Entries.Add(new PatchEntryViewModel("Existing", DialogProject.Empty("Existing")));
        var listPath = Path.Combine(_dir, "order.patchlist");
        var list = PatchList.Empty().WithGameFolder("other")
            .WithEntry(PatchListSerializer.BuildEntry(listPath, Project("Ok")))
            .WithEntry(PatchListSerializer.BuildEntry(listPath, Project("Future", version: 99)));
        PatchListSerializer.SaveToFile(listPath, list);

        vm.LoadFromFile(listPath);

        Assert.Equal(["Existing"], vm.Entries.Select(e => e.ProjectName));
        Assert.Equal("kept", vm.GameFolder);
        Assert.StartsWith("Schema_TooNew", vm.StatusText);
    }

    [Fact]
    public void LoadFromFile_NewerListItself_IsRefused()
    {
        var vm = Vm();
        var listPath = Path.Combine(_dir, "future.patchlist");
        File.WriteAllText(listPath, """{ "SchemaVersion": 99, "GameFolder": "", "Entries": [] }""");

        vm.LoadFromFile(listPath);

        Assert.StartsWith("Schema_TooNew", vm.StatusText);
    }

    [Fact]
    public void LoadFromFile_CurrentList_StillLoads()
    {
        var vm = Vm();
        var listPath = Path.Combine(_dir, "ok.patchlist");
        PatchListSerializer.SaveToFile(listPath,
            PatchList.Empty().WithEntry(PatchListSerializer.BuildEntry(listPath, Project("Ok"))));

        vm.LoadFromFile(listPath);

        Assert.Equal(["Ok"], vm.Entries.Select(e => e.ProjectName));
    }
}
