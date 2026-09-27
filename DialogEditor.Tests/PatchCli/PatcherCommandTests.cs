using System.IO.Compression;
using DialogEditor.Patch;
using DialogEditor.PatchCli;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.PatchCli;

public class PatcherCommandTests : IDisposable
{
    private readonly FakePoe2Game _game = new();
    private readonly string _projDir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose()
    {
        _game.Dispose();
        try { Directory.Delete(_projDir, true); } catch (Exception) { /* best-effort */ }
    }

    private string SaveProject(DialogProject p)
    {
        var path = Path.Combine(_projDir, p.Name + ".dialogproject");
        DialogProjectSerializer.SaveToFile(path, p);
        return path;
    }

    private (int Code, string Out, string Err) Run(params string[] args)
    {
        var o = new StringWriter(); var e = new StringWriter();
        var code = PatcherCommand.Run(args, o, e);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void Apply_ThenApplyAgain_Succeeds()
    {
        var a = SaveProject(FakePoe2Game.ExternalVoMod("A", "a"));
        Assert.Equal(0, Run(_game.Root, a).Code);
        Assert.Equal(0, Run(_game.Root, a).Code);
    }

    [Fact]
    public void Restore_PutsOriginalsBack()
    {
        var before = _game.SnapshotGameData();
        Run(_game.Root, SaveProject(FakePoe2Game.ExternalVoMod("A", "a")));

        var (code, output, _) = Run(_game.Root, "--restore");

        Assert.Equal(0, code);
        Assert.Equal(before, _game.SnapshotGameData());
        Assert.Contains("Restored", output);
    }

    [Fact]
    public void ExternalChange_ExitsWith3_AndNamesTheFlag()
    {
        var a = SaveProject(FakePoe2Game.ExternalVoMod("A", "a"));
        Run(_game.Root, a);
        File.WriteAllText(_game.ConvPath(), FakePoe2Game.OneNodeBundle.Replace("aaaa-0000", "upd"));

        var (code, _, err) = Run(_game.Root, a);

        Assert.Equal(3, code);
        Assert.Contains("test_conv.conversationbundle", err);
        Assert.Contains("--accept-current-files", err);
        Assert.Equal(0, Run(_game.Root, a, "--accept-current-files").Code);
    }

    [Fact]
    public void CorruptManifest_ExitsWith2()
    {
        var dir = Path.Combine(_game.Root, "PillarsDialogPatcher");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), "garbage");

        Assert.Equal(2, Run(_game.Root, "--restore").Code);
    }

    [Fact]
    public void Help_DocumentsRestoreAndExitCode3()
    {
        var (code, output, _) = Run("--help");
        Assert.Equal(0, code);
        Assert.Contains("--restore", output);
        Assert.Contains("--accept-current-files", output);
        Assert.Contains("3 ", output);
    }

    [Fact]
    public void DryRun_ReportsFilesToRestoreFirst()
    {
        var a = SaveProject(FakePoe2Game.ExternalVoMod("A", "a"));
        Run(_game.Root, a);
        var (code, output, _) = Run(_game.Root, a, "--dry-run");
        Assert.Equal(0, code);
        Assert.Contains("restore", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── Newer file format (GitHub issue 62) ──────────────────────────────

    private string SaveNewerProject(string name)
    {
        var path = SaveProject(FakePoe2Game.ExternalVoMod(name, "x"));
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99"));
        return path;
    }

    [Fact]
    public void NewerProject_ExitsWith4_NamesTheFile_AndWritesNothing()
    {
        var before = _game.SnapshotGameData();
        var newer  = SaveNewerProject("Future");

        var (code, _, err) = Run(_game.Root, SaveProject(FakePoe2Game.ExternalVoMod("A", "a")), newer);

        Assert.Equal(4, code);
        Assert.Contains("Future.dialogproject", err);
        Assert.Contains("newer", err);
        Assert.Equal(before, _game.SnapshotGameData());
        Assert.False(Directory.Exists(Path.Combine(_game.Root, "PillarsDialogPatcher")));
    }

    [Fact]
    public void NewerPack_ExitsWith4()
    {
        var project = SaveNewerProject("FuturePack");
        var pack    = Path.Combine(_projDir, "FuturePack.dialogpack");
        using (var zip = ZipFile.Open(pack, ZipArchiveMode.Create))
            zip.CreateEntryFromFile(project, "project.dialogproject");

        Assert.Equal(4, Run(_game.Root, pack).Code);
    }

    [Fact]
    public void Help_DocumentsExitCode4()
        => Assert.Contains("4   ", Run("--help").Out);
}
