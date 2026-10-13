using DialogEditor.Core.Backup;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Backup;

/// Issue 201: the full backup also keeps PoE1's expansion text (data_expansion1, …), and a
/// backup taken before issue 201 is topped up with it once: nothing wrote those folders until then.
public class FullBackupPoe1ExpansionTests : IDisposable
{
    private readonly FakePoe1Game _game = new();
    private readonly string _backup = Path.Combine(Path.GetTempPath(), $"poe1backup_{Guid.NewGuid():N}");

    public void Dispose()
    {
        _game.Dispose();
        try { Directory.Delete(_backup, recursive: true); } catch { /* best-effort */ }
    }

    private string ExpansionTable(string lang) =>
        Path.Combine(_game.Root, "PillarsOfEternity_Data", "data_expansion1", "localized", lang, "text", "conversations", "canonical", "canonical.stringtable");

    private void AddExpansionText(params string[] languages)
    {
        foreach (var lang in languages)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ExpansionTable(lang))!);
            File.WriteAllText(ExpansionTable(lang),
                $"<StringTableFile><Entries><Entry><ID>1</ID><DefaultText>expansion {lang}</DefaultText><FemaleText></FemaleText></Entry></Entries></StringTableFile>");
        }
    }

    private string Snapshot(string lang) =>
        Path.Combine(_backup, "dlc", "data_expansion1", "stringtables", lang, "canonical", "canonical.stringtable");

    /// A backup as taken between issues 198 and 201: format 3, no PoE1 extra folders.
    private void MakeFormatThree()
    {
        var manifest = FullBackup.ReadManifest(_backup)!;
        if (Directory.Exists(Path.Combine(_backup, "dlc"))) Directory.Delete(Path.Combine(_backup, "dlc"), recursive: true);
        File.WriteAllText(Path.Combine(_backup, FullBackup.ManifestFileName),
            System.Text.Json.JsonSerializer.Serialize(manifest with { Format = 3, ExtraFolders = [] }));
    }

    [Fact]
    public async Task Take_KeepsEachExpansionsTextPerLanguage()
    {
        AddExpansionText("en", "de");

        await FullBackup.TakeAsync(_game.Provider, _backup, default);

        Assert.True(File.Exists(Snapshot("en")));
        Assert.True(File.Exists(Snapshot("de")));
        Assert.Contains("dlc/data_expansion1/stringtables/de", FullBackup.ReadManifest(_backup)!.ExtraFolders!);
    }

    [Fact]
    public async Task TopUp_AddsTheExpansionTextAFormatThreeBackupLacks()
    {
        AddExpansionText("en", "de");
        await FullBackup.TakeAsync(_game.Provider, _backup, default);
        MakeFormatThree();

        var added = await FullBackup.TopUpAsync(_game.Provider, _backup, default);

        Assert.Equal(2, added);
        Assert.True(File.Exists(Snapshot("de")));
        Assert.Equal(0, await FullBackup.TopUpAsync(_game.Provider, _backup, default));   // once
    }

    // A backup taken with issue 201 in place already holds every expansion folder the game had.
    [Fact]
    public async Task TopUp_ACurrentBackup_IsLeftAlone()
    {
        AddExpansionText("en");
        await FullBackup.TakeAsync(_game.Provider, _backup, default);
        AddExpansionText("de");   // appears later: only the editor or the patcher could write it

        Assert.Equal(0, await FullBackup.TopUpAsync(_game.Provider, _backup, default));
        Assert.False(File.Exists(Snapshot("de")));
    }

    [Fact]
    public void Patcher_OnAnExpansionLine_WritesTheExpansionTable_AndRestoresTheOriginal()
    {
        AddExpansionText("en");
        var original = File.ReadAllText(ExpansionTable("en"));
        var baseTable = File.ReadAllText(_game.StPath("en"));
        var project = DialogEditor.Patch.DialogProject.Empty("poe1").WithPatch(
            new DialogEditor.Patch.ConversationPatch("canonical", DialogEditor.Patch.ConversationPatch.CurrentSchemaVersion, [], [], [])
            {
                Translations = new Dictionary<string, IReadOnlyList<DialogEditor.Core.Models.NodeTranslation>>
                    { ["en"] = [new DialogEditor.Core.Models.NodeTranslation(1, "changed", "")] },
            });

        var result = DialogEditor.Patch.Install.PatchInstaller.Install(_game.Provider, _game.Root,
            [new DialogEditor.Patch.Install.InstallEntry(project)], new DialogEditor.Patch.Install.InstallOptions());
        Assert.IsType<DialogEditor.Patch.Install.InstallResult.Applied>(result);
        Assert.Contains("changed", File.ReadAllText(ExpansionTable("en")));
        Assert.Equal(baseTable, File.ReadAllText(_game.StPath("en")));

        DialogEditor.Patch.Install.PatchInstaller.Restore(_game.Root);

        Assert.Equal(original, File.ReadAllText(ExpansionTable("en")));
    }
}
