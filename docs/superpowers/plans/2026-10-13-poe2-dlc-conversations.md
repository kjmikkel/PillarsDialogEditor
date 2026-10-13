# PoE2 DLC Conversations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The editor reads, edits, backs up and groups PoE2's DLC conversations, which live in `lax*_exported` folders the editor has never read.

**Architecture:**
- **Data folders:** `Poe2DataRoots` holds the game's own ordered list of data folders. `ConversationFile` records which folder a conversation came from (`DataRoot`).
- **Provider:** `Poe2GameDataProvider` enumerates conversations and resolves string tables, speakers, game data and quests across every folder.
- **Full backup:** it also snapshots the DLC folders (format 3), and older backups are topped up once when the game folder is opened.
- **Dock:** `ConversationTreeBuilder` groups DLC content by title, using the data folder or a `lax…` folder prefix.

**Tech Stack:** .NET 10, C#, Avalonia 11.3, xUnit (headless suite: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`).

**Spec:** `docs/superpowers/specs/2026-10-13-poe2-dlc-conversations-design.md`

## Global Constraints

- **Test runs:** use `dotnet test DialogEditor.Tests --filter "Category!=Gui"`, never a bare `dotnet test`, which launches the GUI tests. Tests run serially.
- **Strings:** no user-visible string is hard-coded. Every one goes in `DialogEditor.Avalonia/Resources/Strings.axaml` and is read through `Loc.Get` in view models.
- **Exceptions:** production code logs every caught exception through `AppLog.Warn` or `AppLog.Error`, except `OperationCanceledException`. No bare `catch { }`.
- **Comments:** they cite "issue 198" where they explain a decision.
- **Line endings:** keep each file's line endings, and keep `git diff --check` clean.
- **Commits:** they end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **PoE1:** no change in behaviour. The PoE1 provider and the White March grouping behave exactly as before.
- **Data folder order:** exactly the game's `ProductConfiguration.PackageDataFolders`:
  `exported, bb_exported, lax1_exported, lax2_exported, lax3_exported, lax4_exported, laxa_exported, laxb_exported, laxc_exported, laxd_exported, unused_exported, laxe_exported, laxf_exported, laxg_exported, laxh_exported, laxi_exported`
  with prefixes `"", bb_, lax1_, lax2_, lax3_, lax4_, laxa_, laxb_, laxc_, laxd_, unused_, laxe_, laxf_, laxg_, laxh_, laxi_`.
- **DLC titles** (approved):
  - lax1: Seeker, Slayer, Survivor
  - lax2: Beast of Winter
  - lax3: The Forgotten Sanctum
  - laxa: Rum Runner's Pack
  - laxb: Beard and Hair Pack
  - laxc: Scalawags Pack
  - laxd: Deck of Many Things Pack
  - laxe: Critical Role Pack
  - laxf: Mega Bosses
  - laxg: Mega Boss – The Hellfire Colossus
  - laxi: Woedica's Book

## Answers to the spec's open questions (from code read during planning)

1. **Folder order.** The game loads bundles folder by folder in the order above (`ResourceManager.LoadBundles`), then the `override` folder. String tables load per data folder by relative path (`StringTableManager.GetStringTablePath(package, …)`). So: dictionaries merge later-wins, and entry lists concatenate in folder order, de-duplicated with the last copy kept.
2. **Voice-over.** DLC voice files sit in the same `Voices/English(US)/<chatter prefix>/<conversation>_<node>.wem` pool, keyed by conversation name (`carver/lax02_01_cv_ice_carver_0008.wem`, `global_god_woedica/laxi_00_si_god_0002.wem`).
   - `VoPathResolver` needs no change.
   - Chatter prefixes must include the DLC speaker bundles (Task 3).
   - `VoAliasIndexService` must scan every folder (Task 4).
   - `VoPackExporter` is path-agnostic.
3. **Backups.**
   - Test Patch (F5/F6) and the patcher (`PatcherBackupStore`, relative to the game folder) back up by file path, so they follow the provider.
   - `FullBackup` snapshots only the base folders, so it needs DLC folders (Task 5).
   - DLC files sit in their own folders, and every prefixed bundle name differs: `lax1_speakers.gamedatabundle`, `lax1_quests.questbundle`.

## Rulings made while planning

- **No path-to-folder mapping.** The spec's `Poe2DataRoots` "maps a file path back to the root that contains it". The plan carries `DataRoot` on `ConversationFile` instead, so nothing ever parses a path back. The spec's "prefix trap" test becomes one about prefix spelling (Task 6). Cost if wrong: one helper later.
- **Global variables stay as they are.** The spec says lookups include DLC global variables. But the base game's lookup reads `design/gamedata/GlobalVariables.csv`, which a GOG install doesn't ship: global variables are `*.globalvariablesbundle` files. Fixing that is a separate defect, so this plan doesn't touch it, and the user is told so it can be filed. Cost if wrong: DLC global-variable suggestions are absent, as base ones are today.
- **Format-1 backups aren't topped up.** A format-1 full backup (no manifest) is left alone: writing a manifest would change how its single text folder is read. Cost if wrong: such a user takes a new backup to cover DLC.

## Review Focus

1. **A base folder and a DLC folder with the same relative path** (`16_wahaki_island` in `exported` and in `lax2_exported`) must stay separate. The DLC one goes under Beast of Winter, the base one stays with the base game. Test: Task 6, `SameFolderInBaseAndDlc_StaySeparate`.
2. **A DLC conversation with no string table in the selected language** opens with missing text, as a base one does (issue 119), without throwing. Test: Task 2, `DlcConversation_MissingLanguage_LoadsWithEmptyText`.
3. **Test Patch then F6 on a DLC conversation** writes only inside its own DLC folder, and F6 restores it byte-for-byte. Test: Task 5, `TestPatch_OnADlcConversation_RestoresByteForByte`.
4. **A format-1 backup and the top-up:** it's left untouched, with no manifest written. Test: Task 5, `TopUp_FormatOneBackup_IsLeftAlone`.
5. **Patcher install and "Remove all mods" on a DLC conversation** return the game to its original bytes. Test: Task 5, `Patcher_OnADlcConversation_RestoresToTheOriginal`.

---

### Task 1: The game's data folders

**Files:**
- Create: `DialogEditor.Core/GameData/Poe2DataRoots.cs`
- Test: `DialogEditor.Tests/GameData/Poe2DataRootsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record Poe2DataRoot(string Folder, string Prefix, string Path) { public string? Key { get; } }`, where `Key` is the prefix without its trailing underscore, or null for the base folder;
  - `public static class Poe2DataRoots` with:
    - `const string DataFolder = "PillarsOfEternityII_Data"`;
    - `const string Base = "exported"`;
    - `IReadOnlyList<(string Folder, string Prefix)> All`;
    - `IReadOnlyList<Poe2DataRoot> Existing(string gameDir)`;
    - `string? KeyOf(string folder)`.

- [ ] **Step 1: Write the failing test**

```csharp
using DialogEditor.Core.GameData;

namespace DialogEditor.Tests.GameData;

/// Issue 198: PoE2 keeps each DLC's data in its own folder; the editor reads them in the
/// game's own order (ProductConfiguration.PackageDataFolders).
public class Poe2DataRootsTests : IDisposable
{
    private readonly string _game = Path.Combine(Path.GetTempPath(), $"roots_{Guid.NewGuid():N}");

    public void Dispose() { try { Directory.Delete(_game, recursive: true); } catch { /* best-effort */ } }

    private void Make(params string[] folders)
    {
        foreach (var f in folders) Directory.CreateDirectory(Path.Combine(_game, Poe2DataRoots.DataFolder, f));
    }

    [Fact]
    public void All_IsTheGamesOrder()
    {
        Assert.Equal(
            ["exported", "bb_exported", "lax1_exported", "lax2_exported", "lax3_exported", "lax4_exported",
             "laxa_exported", "laxb_exported", "laxc_exported", "laxd_exported", "unused_exported",
             "laxe_exported", "laxf_exported", "laxg_exported", "laxh_exported", "laxi_exported"],
            Poe2DataRoots.All.Select(r => r.Folder));
        Assert.All(Poe2DataRoots.All.Skip(1), r => Assert.Equal(r.Folder, r.Prefix + "exported"));
    }

    [Fact]
    public void Existing_ReturnsOnlyFoldersThatExist_InTheGamesOrder()
    {
        Make("laxi_exported", "exported", "lax2_exported");
        var roots = Poe2DataRoots.Existing(_game);
        Assert.Equal(["exported", "lax2_exported", "laxi_exported"], roots.Select(r => r.Folder));
        Assert.Equal(Path.Combine(_game, Poe2DataRoots.DataFolder, "lax2_exported"), roots[1].Path);
        Assert.Equal("lax2_", roots[1].Prefix);
    }

    [Theory]
    [InlineData("exported", null)]
    [InlineData("lax2_exported", "lax2")]
    [InlineData("laxi_exported", "laxi")]
    [InlineData("bb_exported", "bb")]
    [InlineData("not_a_data_folder", null)]
    public void KeyOf_IsThePrefixWithoutItsUnderscore(string folder, string? key)
    {
        Assert.Equal(key, Poe2DataRoots.KeyOf(folder));
        if (key is not null) Assert.Equal(key, new Poe2DataRoot(folder, key + "_", "").Key);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~Poe2DataRootsTests"`
Expected: the build fails with `The name 'Poe2DataRoots' does not exist`.

- [ ] **Step 3: Implement**

```csharp
namespace DialogEditor.Core.GameData;

/// <summary>One of Deadfire's data folders that exists in an install: its name, the prefix its
/// own bundle files carry (<c>lax1_speakers.gamedatabundle</c>), and its full path.</summary>
public sealed record Poe2DataRoot(string Folder, string Prefix, string Path)
{
    /// <summary>The DLC key ("lax2"), or null for the base game's folder.</summary>
    public string? Key => Prefix.Length == 0 ? null : Prefix.TrimEnd('_');
}

/// <summary>
/// Deadfire's data folders (issue 198). The base game's data is in <c>exported</c>, each DLC's
/// in its own <c>&lt;prefix&gt;exported</c> folder beside it, with the same layout (design/,
/// localized/). The list and its order are the game's own,
/// <c>ProductConfiguration.PackageDataFolders</c> and <c>PackageDataPrefixes</c>: the game loads
/// the folders in this order, so a later folder's entry wins over an earlier one's.
/// </summary>
public static class Poe2DataRoots
{
    public const string DataFolder = "PillarsOfEternityII_Data";
    public const string Base = "exported";

    public static readonly IReadOnlyList<(string Folder, string Prefix)> All =
    [
        ("exported", ""), ("bb_exported", "bb_"), ("lax1_exported", "lax1_"), ("lax2_exported", "lax2_"),
        ("lax3_exported", "lax3_"), ("lax4_exported", "lax4_"), ("laxa_exported", "laxa_"),
        ("laxb_exported", "laxb_"), ("laxc_exported", "laxc_"), ("laxd_exported", "laxd_"),
        ("unused_exported", "unused_"), ("laxe_exported", "laxe_"), ("laxf_exported", "laxf_"),
        ("laxg_exported", "laxg_"), ("laxh_exported", "laxh_"), ("laxi_exported", "laxi_"),
    ];

    public static IReadOnlyList<Poe2DataRoot> Existing(string gameDir) =>
        All.Select(a => new Poe2DataRoot(a.Folder, a.Prefix, System.IO.Path.Combine(gameDir, DataFolder, a.Folder)))
           .Where(r => Directory.Exists(r.Path))
           .ToList();

    public static string? KeyOf(string folder) =>
        All.Where(a => a.Folder == folder && a.Prefix.Length > 0).Select(a => a.Prefix.TrimEnd('_')).FirstOrDefault();
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~Poe2DataRootsTests"`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/GameData/Poe2DataRoots.cs DialogEditor.Tests/GameData/Poe2DataRootsTests.cs
git commit -m "feat: Deadfire's data folders, in the game's own order (#198)"
```

---

### Task 2: Conversations and text from every data folder

**Files:**
- Modify: `DialogEditor.Core/GameData/ConversationFile.cs`
- Modify: `DialogEditor.Core/GameData/Poe2GameDataProvider.cs`: `EnumerateConversations`, `BuildConversationFile`, `LoadConversation`, both `GetStringTablePath` overloads, `StringTablePathFor`
- Modify: `DialogEditor.Tests/Helpers/FakePoe2Game.cs` (adds `AddDlcConversation`)
- Test: `DialogEditor.Tests/GameData/Poe2DlcProviderTests.cs`

**Interfaces:**
- Consumes: `Poe2DataRoots.Existing`, `Poe2DataRoot` (Task 1).
- Produces:
  - `ConversationFile(string Name, string FolderPath, string ConversationPath, string StringTablePath, string DataRoot = ConversationFile.BaseDataRoot)`, with `public const string BaseDataRoot = "exported";`;
  - `FakePoe2Game.AddDlcConversation(string dataFolder, string folder, string name, params string[] languages)`, which writes `OneNodeBundle` plus `StringTable($"{name} {lang}")` per language and returns the bundle path;
  - `Poe2GameDataProvider` lists DLC conversations with `DataRoot` set to the folder name.

- [ ] **Step 1: Add the fixture helper and write the failing tests**

In `FakePoe2Game.cs`, add after `StPath`:

```csharp
    /// A DLC conversation (issue 198) in its own data folder, e.g. "lax2_exported", with a
    /// string table per language whose text is "<name> <lang>". Returns the bundle path.
    public string AddDlcConversation(string dataFolder, string folder, string name, params string[] languages)
    {
        var dlc  = Path.Combine(DataRoot, dataFolder);
        var conv = Path.Combine(dlc, "design", "conversations", folder, name + ".conversationbundle");
        Directory.CreateDirectory(Path.GetDirectoryName(conv)!);
        File.WriteAllText(conv, OneNodeBundle);
        foreach (var lang in languages)
        {
            var st = Path.Combine(dlc, "localized", lang, "text", "conversations", folder, name + ".stringtable");
            Directory.CreateDirectory(Path.GetDirectoryName(st)!);
            File.WriteAllText(st, StringTable($"{name} {lang}"));
        }
        return conv;
    }
```

Create `DialogEditor.Tests/GameData/Poe2DlcProviderTests.cs`:

```csharp
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.GameData;

/// Issue 198: PoE2's DLC conversations live in their own data folders (lax2_exported, …),
/// which the editor never read.
public class Poe2DlcProviderTests : IDisposable
{
    private readonly FakePoe2Game _game = new("en", "de");
    public void Dispose() => _game.Dispose();

    [Fact]
    public void Enumerate_ListsBaseAndDlcConversations_WithTheirDataFolder()
    {
        _game.AddDlcConversation("lax2_exported", "lax02_01_iceberg_dungeon", "lax02_01_cv_ice_carver", "en", "de");

        var files = _game.Provider.EnumerateConversations();

        var base_ = files.Single(f => f.Name == "test_conv");
        Assert.Equal(ConversationFile.BaseDataRoot, base_.DataRoot);
        var dlc = files.Single(f => f.Name == "lax02_01_cv_ice_carver");
        Assert.Equal("lax2_exported", dlc.DataRoot);
        Assert.Equal("lax02_01_iceberg_dungeon", dlc.FolderPath);
        Assert.Equal(["test_conv", "lax02_01_cv_ice_carver"], files.Select(f => f.Name));   // game's folder order
    }

    [Fact]
    public void DlcConversation_LoadsItsOwnText_InEachLanguage()
    {
        _game.AddDlcConversation("lax2_exported", "lax02_01_iceberg_dungeon", "lax02_01_cv_ice_carver", "en", "de");
        var provider = _game.Provider;
        var file = provider.FindConversation("lax02_01_cv_ice_carver")!;

        Assert.Equal("lax02_01_cv_ice_carver en", provider.LoadConversation(file).Strings.Entries.Single().DefaultText);
        provider.Language = "de";
        Assert.Equal("lax02_01_cv_ice_carver de", provider.LoadConversation(file).Strings.Entries.Single().DefaultText);
        Assert.StartsWith(Path.Combine(_game.DataRoot, "lax2_exported", "localized", "de"), provider.GetStringTablePath(file, "de"));
    }

    [Fact]
    public void DlcConversation_MissingLanguage_LoadsWithEmptyText()
    {
        _game.AddDlcConversation("laxi_exported", "laxi", "laxi_00_si_god", "en");   // no "de"
        var provider = _game.Provider;
        provider.Language = "de";

        var conversation = provider.LoadConversation(provider.FindConversation("laxi_00_si_god")!);

        Assert.Single(conversation.Nodes);
        Assert.Empty(conversation.Strings.Entries);
    }

    [Fact]
    public void SavingADlcConversation_WritesOnlyInItsOwnFolder()
    {
        var path = _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");
        var provider = _game.Provider;
        var file = provider.FindConversation("lax02_cv_x")!;
        var before = _game.SnapshotGameData();

        provider.SaveConversation(file, ConversationSnapshotBuilder.Build(provider.LoadConversation(file)));

        var changed = _game.SnapshotGameData()
            .Where(kv => !before.TryGetValue(kv.Key, out var v) || v != kv.Value)
            .Select(kv => kv.Key).ToList();
        Assert.NotEmpty(changed);
        Assert.All(changed, k => Assert.StartsWith("lax2_exported", k));
        Assert.True(File.Exists(path));
    }
}
```

`Strings.Entries` and `DefaultText` are the `StringTable`/`StringEntry` members the rest of the suite uses. If the names differ, use the ones in `DialogEditor.Core/Models/StringTable.cs`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~Poe2DlcProviderTests"`
Expected: the build fails with `'ConversationFile' does not contain a definition for 'DataRoot'`.

- [ ] **Step 3: Implement `ConversationFile.DataRoot`**

Replace `DialogEditor.Core/GameData/ConversationFile.cs` with:

```csharp
namespace DialogEditor.Core.GameData;

public record ConversationFile(
    string Name,
    string FolderPath,
    string ConversationPath,
    string StringTablePath,
    // The data folder the conversation was read from (issue 198): "exported" for the base
    // game, "lax2_exported" etc. for PoE2 DLC. PoE1 has one, so it is always the base.
    string DataRoot = ConversationFile.BaseDataRoot
)
{
    public const string BaseDataRoot = "exported";
}
```

- [ ] **Step 4: Implement enumeration and per-folder string tables**

In `Poe2GameDataProvider.cs`, add below `ConversationsRoot`:

```csharp
    // Every data folder the install has, base first, in the game's order (issue 198).
    private IReadOnlyList<Poe2DataRoot> Roots => Poe2DataRoots.Existing(rootPath);
    private static string ConversationsOf(Poe2DataRoot root) => Path.Combine(root.Path, "design", "conversations");
```

Replace `EnumerateConversations` and `BuildConversationFile` with:

```csharp
    public IReadOnlyList<ConversationFile> EnumerateConversations() =>
        Roots.SelectMany(root =>
            {
                var conversations = ConversationsOf(root);
                return Directory.Exists(conversations)
                    ? Directory.EnumerateFiles(conversations, "*.conversationbundle", SearchOption.AllDirectories)
                        .Select(path => BuildConversationFile(root, conversations, path))
                        .OrderBy(f => f.FolderPath).ThenBy(f => f.Name)
                    : [];
            })
            .ToList();

    private static ConversationFile BuildConversationFile(Poe2DataRoot root, string conversationsRoot, string convPath)
    {
        var relative = Path.GetRelativePath(conversationsRoot, convPath);
        return new ConversationFile(
            Name: Path.GetFileNameWithoutExtension(convPath),
            FolderPath: Path.GetDirectoryName(relative) ?? string.Empty,
            ConversationPath: convPath,
            StringTablePath: string.Empty,
            DataRoot: root.Folder);
    }
```

Replace the two `GetStringTablePath` overloads and `StringTablePathFor` with:

```csharp
    public string GetStringTablePath(ConversationFile file)
        => StringTablePathFor(file, Language);

    public string GetStringTablePath(ConversationFile file, string language)
        => StringTablePathFor(file, language);

    // A conversation's text sits in its own data folder's localized/, under the same relative
    // path as the bundle (issue 198; the game's StringTableManager does the same per package).
    private string StringTablePathFor(ConversationFile file, string language)
    {
        var dataRoot   = Path.Combine(rootPath, Poe2DataRoots.DataFolder, file.DataRoot);
        var relative   = Path.GetRelativePath(Path.Combine(dataRoot, "design", "conversations"), file.ConversationPath);
        var withoutExt = Path.ChangeExtension(relative, null);
        return Path.Combine(dataRoot, "localized", language, "text", "conversations", withoutExt + ".stringtable");
    }
```

In `LoadConversation`, change `var stPath = StringTablePathFor(file.ConversationPath, Language);` to `var stPath = StringTablePathFor(file, Language);`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&(FullyQualifiedName~Poe2DlcProviderTests|FullyQualifiedName~Poe2GameDataProvider|FullyQualifiedName~Poe1GameDataProvider)"`
Expected: PASS. The existing PoE1 and PoE2 provider tests stay green.

- [ ] **Step 6: Run the full suite and commit**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: PASS, with no failures.

```bash
git add DialogEditor.Core/GameData/ConversationFile.cs DialogEditor.Core/GameData/Poe2GameDataProvider.cs DialogEditor.Tests/Helpers/FakePoe2Game.cs DialogEditor.Tests/GameData/Poe2DlcProviderTests.cs
git commit -m "feat: PoE2 reads DLC conversations and their text from every data folder (#198)"
```

---

### Task 2b (added during execution): shared paths and cross-folder text

See the spec's Amendment and the ledger's Task 2b ruling.

- `EnumerateConversations` lists one file per relative path, from the last folder that has it.
- `LoadConversation` merges every folder's table per line, the later folder winning.
- `GetStringTablePath` returns the last folder that has a table, else the bundle's folder.
- `StringTable.Entries` is added.
- Tests in `Poe2DlcProviderTests`: `ASharedPath_IsListedOnce_FromTheLastFolderThatHasIt`, `Text_IsMergedPerLine_TheLaterFolderWinning`, `TextIsWritten_ToTheLastFolderWithATable_ElseTheBundlesFolder`, `TheLastBundleWithoutText_TakesItsTextFromAnEarlierFolder`, `NoTableAnywhere_IsMissing`.

---

### Task 3: Speakers, chatter prefixes, game data and quests from every data folder

**Files:**
- Modify: `DialogEditor.Core/GameData/Poe2GameDataProvider.cs`: `SpeakersBundle`, `LoadSpeakerNames`, `LoadChatterPrefixes`, `LoadGameDataNames`
- Test: `DialogEditor.Tests/GameData/Poe2DlcProviderTests.cs` (append)

**Interfaces:**
- Consumes: `Roots` and `Poe2DataRoot.Prefix` (Tasks 1–2).
- Produces: no new public API. The existing methods merge every folder, later folder wins.

- [ ] **Step 1: Write the failing tests** (append to `Poe2DlcProviderTests`)

```csharp
    private string GameData(string dataFolder)
    {
        var dir = Path.Combine(_game.DataRoot, dataFolder, "design", "gamedata");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Speakers(params (string Id, string DebugName)[] speakers) =>
        "{\"GameDataObjects\":[" +
        string.Join(",", speakers.Select(s => $"{{\"ID\":\"{s.Id}\",\"DebugName\":\"{s.DebugName}\"}}")) +
        "]}";

    [Fact]
    public void SpeakerNames_IncludeEachDlcsSpeakers_LaterFolderWins()
    {
        File.WriteAllText(Path.Combine(GameData("exported"), "speakers.gamedatabundle"),
            Speakers(("aaaaaaaa-0000-0000-0000-000000000001", "SPK_Base"), ("aaaaaaaa-0000-0000-0000-000000000003", "SPK_Old")));
        File.WriteAllText(Path.Combine(GameData("lax2_exported"), "lax2_speakers.gamedatabundle"),
            Speakers(("aaaaaaaa-0000-0000-0000-000000000002", "SPK_Carver"), ("aaaaaaaa-0000-0000-0000-000000000003", "SPK_New")));

        var names = _game.Provider.LoadSpeakerNames();

        Assert.Equal("Base",   names["aaaaaaaa-0000-0000-0000-000000000001"]);
        Assert.Equal("Carver", names["aaaaaaaa-0000-0000-0000-000000000002"]);
        Assert.Equal("New",    names["aaaaaaaa-0000-0000-0000-000000000003"]);
    }

    [Fact]
    public void Quests_IncludeEachDlcsQuestBundle()
    {
        void Quests(string dataFolder, string file, string id, string name)
        {
            var dir = Path.Combine(_game.DataRoot, dataFolder, "design", "quests");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, file),
                $"{{\"Hash\":0,\"Quests\":[{{\"ID\":\"{id}\",\"Filename\":\"quests/{name}.quest\"}}]}}");
        }
        Quests("exported", "quests.questbundle", "11111111-0000-0000-0000-000000000001", "base_quest");
        Quests("lax1_exported", "lax1_quests.questbundle", "11111111-0000-0000-0000-000000000002", "arena_quest");

        var quests = _game.Provider.LoadGameDataNames()["Quest"];

        Assert.Contains(quests, q => q.Id == "11111111-0000-0000-0000-000000000002");
        Assert.Contains(quests, q => q.Id == "11111111-0000-0000-0000-000000000001");
    }

    [Fact]
    public void ConversationLookup_IncludesDlcConversations()
    {
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");
        var bundle = Path.Combine(_game.DataRoot, "lax2_exported", "design", "conversations", "x", "lax02_cv_x.conversationbundle");
        File.WriteAllText(bundle, File.ReadAllText(bundle).Replace("{\"Conversations\": [{",
            "{\"Conversations\": [{\"ID\": \"22222222-0000-0000-0000-000000000002\","));

        var conversations = _game.Provider.LoadGameDataNames()["Conversation"];

        Assert.Contains(conversations, c => c.Name == "lax02_cv_x");
    }
```

The quest JSON shape follows `Poe2QuestBundleParser`, and the speaker shape follows `Poe2GameDataProviderTests.SpeakersJson`. If the quest parser reads the name from a field other than `Filename`, match the shape used in `DialogEditor.Tests/Parsing/Poe2QuestBundleParserTests.cs`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~Poe2DlcProviderTests"`
Expected: the three new tests FAIL: the speaker is missing, the quest is missing, and the `Conversation` key or entry is missing.

- [ ] **Step 3: Implement**

In `Poe2GameDataProvider.cs`, replace `SpeakersBundle`, `LoadSpeakerNames` and `LoadChatterPrefixes` with:

```csharp
    // A bundle every data folder may have, named with that folder's prefix
    // (speakers.gamedatabundle, lax2_speakers.gamedatabundle, …), in the game's order (issue 198).
    private IEnumerable<string> BundlesNamed(string subfolder, string name) =>
        Roots.Select(r => Path.Combine(r.Path, "design", subfolder, r.Prefix + name)).Where(File.Exists);

    // Later folders win, as the game loads them in this order.
    private static IReadOnlyDictionary<string, string> MergeLaterWins(IEnumerable<IReadOnlyDictionary<string, string>> maps)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in maps)
            foreach (var (key, value) in map) result[key] = value;
        return result;
    }

    public IReadOnlyDictionary<string, string> LoadSpeakerNames() =>
        MergeLaterWins(BundlesNamed("gamedata", "speakers.gamedatabundle").Select(Poe2SpeakerNameParser.ParseFile));

    public IReadOnlyDictionary<string, string> LoadChatterPrefixes() =>
        MergeLaterWins(BundlesNamed("gamedata", "speakers.gamedatabundle").Select(Poe2SpeakerNameParser.ParseChatterPrefixesFile));
```

If `Poe2SpeakerNameParser.ParseFile` returns `Dictionary<string,string>`, the method-group conversion to `IReadOnlyDictionary` works as written. If not, use a lambda: `.Select(f => (IReadOnlyDictionary<string, string>)Poe2SpeakerNameParser.ParseFile(f))`.

In `LoadGameDataNames`:

- **Phase 1:** replace `if (Directory.Exists(gdRoot))` and its `foreach (var file in Directory.EnumerateFiles(gdRoot, "*.gamedatabundle"))` with a loop over every folder's `gamedata`. Keep the body, which does the bucketing, composition and `ComposeInto` calls:

```csharp
        var gameDataFolders = Roots.Select(r => Path.Combine(r.Path, "design", "gamedata")).Where(Directory.Exists).ToList();
        if (gameDataFolders.Count > 0)
        {
            var byKind = new Dictionary<string, List<GameDataEntry>>(StringComparer.Ordinal);
            foreach (var file in gameDataFolders.SelectMany(d => Directory.EnumerateFiles(d, "*.gamedatabundle")))
            {
```

  Drop the now-unused `var gdRoot = GameDataRoot;` only once Phase 2 no longer uses it.

- **Phase 2:** `FactionBundle`, Class and WeaponType read every folder's prefixed bundle:

```csharp
        void FactionBundle(string kind, string typeFilter, Func<string, string>? clean = null)
        {
            var entries = BundlesNamed("gamedata", "factions.gamedatabundle")
                .SelectMany(f => Poe2GameDataBundleParser.ParseFile(f, clean, typeFilter)).ToList();
            if (entries.Count > 0) result[kind] = LastOfEach(entries);
        }
```

```csharp
        var classEntries = BundlesNamed("gamedata", "characters.gamedatabundle")
            .SelectMany(f => Poe2GameDataBundleParser.ParseFile(f, typeFilter: "BaseStatsGameData", componentFilter: IsPlayerClassComponent))
            .ToList();
        if (classEntries.Count > 0) result["Class"] = LastOfEach(classEntries);

        var weaponEntries = BundlesNamed("gamedata", "global.gamedatabundle")
            .SelectMany(f => Poe2GameDataBundleParser.ParseFile(f, typeFilter: "WeaponTypeGameData"))
            .Select(e => new GameDataEntry(Id: string.Empty, Name: e.Name))
            .ToList();
        if (weaponEntries.Count > 0) result["WeaponType"] = LastOfEach(weaponEntries);
```

- **Quests:**

```csharp
        var quests = BundlesNamed("quests", "quests.questbundle").SelectMany(Poe2QuestBundleParser.ParseFile).ToList();
        if (quests.Count > 0) result["Quest"] = LastOfEach(quests);
```

- **Global variables:** leave `GlobalVariables.csv` as is (see the ruling above). It reads the base `GameDataRoot`, so keep `GameDataRoot`.
- **Conversations:**

```csharp
        var conversations = Roots.Select(ConversationsOf).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.conversationbundle", SearchOption.AllDirectories))
            .Select(path => (
                id:   Poe2ConversationParser.ParseRootId(File.ReadAllText(path)),
                name: Path.GetFileNameWithoutExtension(path)))
            .Where(t => !string.IsNullOrWhiteSpace(t.id))
            .Select(t => new GameDataEntry(Id: t.id!, Name: t.name))
            .OrderBy(e => e.Name)
            .ToList();
        if (conversations.Count > 0) result["Conversation"] = conversations;
```

Add the helper next to `IsPlayerClassComponent`:

```csharp
    // Entries merged from several data folders: the last copy of each (same ID and name) wins,
    // as the game loads later folders after earlier ones (issue 198).
    private static IReadOnlyList<GameDataEntry> LastOfEach(IReadOnlyList<GameDataEntry> entries) =>
        entries.Reverse().DistinctBy(e => (e.Id, e.Name)).Reverse().ToList();
```

Check the return type of `Poe2GameDataBundleParser.ParseFile` and `Poe2QuestBundleParser.ParseFile`. If they return `List<GameDataEntry>`, `SelectMany` works as written.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&(FullyQualifiedName~Poe2DlcProviderTests|FullyQualifiedName~Poe2GameDataProviderTests)"`
Expected: PASS, including every existing `LoadGameDataNames_*` test.

- [ ] **Step 5: Run the full suite and commit**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: PASS.

```bash
git add DialogEditor.Core/GameData/Poe2GameDataProvider.cs DialogEditor.Tests/GameData/Poe2DlcProviderTests.cs
git commit -m "feat: PoE2 speakers, game data and quests include every DLC data folder (#198)"
```

---

### Task 4: The VO alias index scans every data folder

**Files:**
- Modify: `DialogEditor.ViewModels/Services/VoAliasIndexService.cs:37`
- Test: the existing VO alias index test file (`grep -rl "VoAliasIndexService.Rebuild" DialogEditor.Tests`); append there.

**Interfaces:**
- Consumes: `Poe2DataRoots.Existing` (Task 1).

- [ ] **Step 1: Write the failing test** (append to the alias index tests; reuse that file's helpers for a temp game root if it has them)

```csharp
    [Fact]
    public void Rebuild_IndexesDlcConversations()   // issue 198
    {
        var game = Path.Combine(Path.GetTempPath(), $"alias_{Guid.NewGuid():N}");
        try
        {
            var conv = Path.Combine(game, "PillarsOfEternityII_Data", "lax2_exported", "design", "conversations", "x");
            Directory.CreateDirectory(conv);
            File.WriteAllText(Path.Combine(conv, "lax02_cv_x.conversationbundle"),
                """{"Nodes":[{"NodeID":3,"ExternalVO":"carver/lax02_01_cv_ice_carver_0008"}]}""");

            VoAliasIndexService.Rebuild(game);

            Assert.Contains(VoAliasIndexService.Lookup("carver/lax02_01_cv_ice_carver_0008"),
                r => r.Conversation == "lax02_cv_x" && r.NodeId == 3);
        }
        finally { try { Directory.Delete(game, recursive: true); } catch { /* best-effort */ } }
    }
```

`ScanFile` reads a top-level `Nodes` array, so the fixture has one. Use the index's real lookup method name. Check `VoAliasIndexService` for its public query (for example `RefsFor`) and adjust the call.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~Rebuild_IndexesDlcConversations"`
Expected: FAIL, with no references found.

- [ ] **Step 3: Implement.** Replace line 37 (`AddTree(Path.Combine(gameRoot, "PillarsOfEternityII_Data", "exported", "design", "conversations"));`) with:

```csharp
        // Every data folder, base first, in the game's order (issue 198), then mods.
        foreach (var root in DialogEditor.Core.GameData.Poe2DataRoots.Existing(gameRoot))
            AddTree(Path.Combine(root.Path, "design", "conversations"));
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~VoAlias"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/VoAliasIndexService.cs DialogEditor.Tests
git commit -m "feat: the VO alias index includes PoE2 DLC conversations (#198)"
```

---

### Task 5: Backups cover the DLC folders

**Files:**
- Create: `DialogEditor.Core/GameData/ExtraBackupFolder.cs`
- Modify: `DialogEditor.Core/GameData/IGameDataProvider.cs` (default method)
- Modify: `DialogEditor.Core/GameData/Poe2GameDataProvider.cs` (override)
- Modify: `DialogEditor.Core/Backup/FullBackup.cs`: manifest, `TakeAsync`, new `TopUpAsync`, `PlanRestore`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`: `LoadDirectory` known-folder branch, new `TopUpBackupAsync`
- Test: `DialogEditor.Tests/Backup/FullBackupDlcTests.cs`, `DialogEditor.Tests/ViewModels/MainWindowViewModelDlcBackupTests.cs`

**Interfaces:**
- Consumes: `Roots` (Task 2), `FakePoe2Game.AddDlcConversation` (Task 2).
- Produces:
  - `public sealed record ExtraBackupFolder(string SnapshotPath, string LivePath);`
  - `IGameDataProvider.GetExtraBackupFolders()` returning `IReadOnlyList<ExtraBackupFolder>`, default `[]`;
  - `FullBackupManifest(int Format, string GameId, IReadOnlyList<string> Languages, IReadOnlyList<string>? ExtraFolders = null)`;
  - `FullBackup.TopUpAsync(IGameDataProvider, string backupRoot, CancellationToken)` returning `Task<int>`.

- [ ] **Step 1: Write the failing tests**

`DialogEditor.Tests/Backup/FullBackupDlcTests.cs`:

```csharp
using DialogEditor.Core.Backup;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Backup;

/// Issue 198: the full backup also keeps the PoE2 DLC data folders, and an older backup is
/// topped up with them once (nothing could have written them before issue 198).
public class FullBackupDlcTests : IDisposable
{
    private readonly FakePoe2Game _game = new("en", "de");
    private readonly string _backup = Path.Combine(Path.GetTempPath(), $"dlcbackup_{Guid.NewGuid():N}");

    public void Dispose()
    {
        _game.Dispose();
        try { Directory.Delete(_backup, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task Take_KeepsEachDlcsConversationsAndText()
    {
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en", "de");

        await FullBackup.TakeAsync(_game.Provider, _backup, default);

        Assert.True(File.Exists(Path.Combine(_backup, "dlc", "lax2_exported", "conversations", "x", "lax02_cv_x.conversationbundle")));
        Assert.True(File.Exists(Path.Combine(_backup, "dlc", "lax2_exported", "stringtables", "de", "x", "lax02_cv_x.stringtable")));
        Assert.Contains("dlc/lax2_exported/conversations", FullBackup.ReadManifest(_backup)!.ExtraFolders!);
    }

    [Fact]
    public async Task PlanRestore_PairsEachDlcFolderWithItsLiveFolder()
    {
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");
        await FullBackup.TakeAsync(_game.Provider, _backup, default);

        var plan = FullBackup.PlanRestore(_game.Provider, _backup, voicesRoot: null);

        Assert.Contains(plan.Pairs, p =>
            p.Snapshot == Path.Combine(_backup, "dlc/lax2_exported/conversations") &&
            p.Live == Path.Combine(_game.DataRoot, "lax2_exported", "design", "conversations"));
    }

    [Fact]
    public async Task TopUp_AddsTheDlcFoldersAnOlderBackupLacks()
    {
        await FullBackup.TakeAsync(_game.Provider, _backup, default);   // no DLC yet
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");

        var added = await FullBackup.TopUpAsync(_game.Provider, _backup, default);

        Assert.Equal(2, added);   // conversations + "en" text
        Assert.True(File.Exists(Path.Combine(_backup, "dlc", "lax2_exported", "conversations", "x", "lax02_cv_x.conversationbundle")));
        Assert.Equal(0, await FullBackup.TopUpAsync(_game.Provider, _backup, default));   // once
    }

    [Fact]
    public async Task TopUp_FormatOneBackup_IsLeftAlone()
    {
        Directory.CreateDirectory(Path.Combine(_backup, "conversations"));   // format 1: no manifest
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");

        Assert.Equal(0, await FullBackup.TopUpAsync(_game.Provider, _backup, default));
        Assert.False(File.Exists(Path.Combine(_backup, FullBackup.ManifestFileName)));
        Assert.False(Directory.Exists(Path.Combine(_backup, "dlc")));
    }

    [Fact]
    public void Patcher_OnADlcConversation_RestoresToTheOriginal()   // Review Focus 5
    {
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");
        var original = _game.SnapshotGameData();
        var project = DialogEditor.Patch.DialogProject.Empty("dlc").WithPatch(
            new DialogEditor.Patch.ConversationPatch("lax02_cv_x", DialogEditor.Patch.ConversationPatch.CurrentSchemaVersion, [], [], [])
            {
                NodeComments = new Dictionary<int, string> { [1] = "note" },
                Translations = new Dictionary<string, IReadOnlyList<DialogEditor.Patch.NodeTranslation>>
                    { ["en"] = [new DialogEditor.Patch.NodeTranslation(1, "changed", "")] },
            });

        var result = DialogEditor.Patch.Install.PatchInstaller.Install(_game.Provider, _game.Root,
            [new DialogEditor.Patch.Install.InstallEntry(project)], new DialogEditor.Patch.Install.InstallOptions());
        Assert.IsType<DialogEditor.Patch.Install.InstallResult.Applied>(result);
        Assert.Contains("changed", File.ReadAllText(Path.Combine(_game.DataRoot, "lax2_exported", "localized", "en", "text", "conversations", "x", "lax02_cv_x.stringtable")));

        DialogEditor.Patch.Install.PatchInstaller.Restore(_game.Root);

        Assert.Equal(original, _game.SnapshotGameData());
    }
}
```

`NodeTranslation` and `NodeComments` follow `MainWindowViewModelFullRestoreTests.EditExisting`. Use the same namespaces and constructor arguments that file uses. `SnapshotGameData` covers only the game's data folder, so the patcher's own backup folder doesn't count.

`DialogEditor.Tests/ViewModels/MainWindowViewModelDlcBackupTests.cs` uses the harness from `MainWindowViewModelFullRestoreTests` (`Inject`, `SetProject` by reflection):

```csharp
using DialogEditor.Core.Backup;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Issue 198: Test Patch and F6 on a DLC conversation, and the backup top-up on opening a
/// known game folder.
public class MainWindowViewModelDlcBackupTests : IDisposable
{
    private readonly FakePoe2Game _game = new("en");
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_dlc_{Guid.NewGuid():N}.json");
    private readonly string _backupPick   = Path.Combine(Path.GetTempPath(), $"dlc_pick_{Guid.NewGuid():N}");

    public MainWindowViewModelDlcBackupTests()
    {
        Loc.Configure(new StubStringProvider());
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { File.Delete(_settingsPath); } catch { /* best-effort */ }
        try { Directory.Delete(_backupPick, recursive: true); } catch { /* best-effort */ }
        _game.Dispose();
    }

    private static void Inject(MainWindowViewModel vm, string field, object? value) =>
        typeof(MainWindowViewModel).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, value);

    private static object? Invoke(MainWindowViewModel vm, string method, params object?[] args) =>
        typeof(MainWindowViewModel).GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, args);

    [Fact]
    public async Task TestPatch_OnADlcConversation_RestoresByteForByte()   // Review Focus 3
    {
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");
        var original = _game.SnapshotGameData();
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        Inject(vm, "_provider", _game.Provider);
        Inject(vm, "_currentGameDirectory", _game.Root);
        Inject(vm, "_activeGameId", "poe2");
        Invoke(vm, "SetProject", DialogProject.Empty("dlc").WithPatch(
            new ConversationPatch("lax02_cv_x", ConversationPatch.CurrentSchemaVersion, [], [], [])
            { NodeComments = new Dictionary<int, string> { [1] = "note" } }));

        await vm.TestPatchCommand.ExecuteAsync(null);
        var written = _game.SnapshotGameData().Where(kv => !original.TryGetValue(kv.Key, out var v) || v != kv.Value).Select(kv => kv.Key).ToList();
        Assert.NotEmpty(written);
        Assert.All(written, k => Assert.StartsWith("lax2_exported", k));

        await vm.RestoreTestPatchCommand.ExecuteAsync(null);
        Assert.Equal(original, _game.SnapshotGameData());
    }

    [Fact]
    public async Task OpeningAKnownGameFolder_TopsUpTheLatestBackup()
    {
        var backupRoot = Path.Combine(_backupPick, "2026-09-28T10-00");
        await FullBackup.TakeAsync(_game.Provider, backupRoot, default);   // before the DLC existed
        AppSettings.SetBackupPath(_game.Root, _backupPick);
        AppSettings.MarkGameDirectoryKnown(_game.Root);
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");

        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        Invoke(vm, "LoadDirectory", _game.Root);

        var expected = Path.Combine(backupRoot, "dlc", "lax2_exported", "conversations", "x", "lax02_cv_x.conversationbundle");
        for (var i = 0; i < 50 && !File.Exists(expected); i++) await Task.Delay(100);
        Assert.True(File.Exists(expected));
    }
}
```

Use the real names of the Test Patch and F6 commands: `grep -n "RelayCommand" DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs | grep -i "test\|restore"`. The existing `MainWindowViewModelTestPatchTests` invokes them; copy its calls exactly.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&(FullyQualifiedName~FullBackupDlcTests|FullyQualifiedName~MainWindowViewModelDlcBackupTests)"`
Expected: the build fails (`ExtraFolders`, `TopUpAsync` don't exist). Once the stubs compile, the DLC backup assertions fail. `TestPatch_OnADlcConversation_RestoresByteForByte` and `Patcher_OnADlcConversation_RestoresToTheOriginal` may already pass after Task 2: they pin behaviour that follows the provider's paths. Record that in the ledger as expected.

- [ ] **Step 3: Implement the provider side**

`DialogEditor.Core/GameData/ExtraBackupFolder.cs`:

```csharp
namespace DialogEditor.Core.GameData;

/// <summary>A game folder the full backup keeps beyond the base conversations and text (PoE2 DLC
/// data folders, issue 198). <see cref="SnapshotPath"/> is relative to the backup folder.</summary>
public sealed record ExtraBackupFolder(string SnapshotPath, string LivePath);
```

`IGameDataProvider.cs`, after `GetStringTablesRoot`:

```csharp
    /// Further folders the full backup keeps: PoE2's DLC data folders (issue 198). Empty for
    /// PoE1, whose DLC sits inside the base folders.
    IReadOnlyList<ExtraBackupFolder> GetExtraBackupFolders() => [];
```

`Poe2GameDataProvider.cs`, after `GetBackupRoots`:

```csharp
    // Each DLC data folder's conversations and its text per installed language (issue 198).
    public IReadOnlyList<ExtraBackupFolder> GetExtraBackupFolders() =>
        Roots.Where(r => r.Key is not null).SelectMany(r =>
        {
            var folders = new List<ExtraBackupFolder>();
            if (Directory.Exists(ConversationsOf(r)))
                folders.Add(new($"dlc/{r.Folder}/conversations", ConversationsOf(r)));
            foreach (var lang in AvailableLanguages)
            {
                var text = Path.Combine(r.Path, "localized", lang, "text", "conversations");
                if (Directory.Exists(text)) folders.Add(new($"dlc/{r.Folder}/stringtables/{lang}", text));
            }
            return folders;
        }).ToList();
```

- [ ] **Step 4: Implement the backup side** (`FullBackup.cs`)

The manifest record becomes:

```csharp
/// <summary>What a full backup folder holds. Format 2 (issue 123) adds the per-language
/// stringtable folders; format 3 (issue 198) adds <see cref="ExtraFolders"/>, the PoE2 DLC data
/// folders, as paths relative to the backup. A backup without a manifest is format 1.</summary>
public sealed record FullBackupManifest(int Format, string GameId, IReadOnlyList<string> Languages,
                                        IReadOnlyList<string>? ExtraFolders = null);
```

Set `private const int CurrentFormat = 3;`.

In `TakeAsync`, before the manifest is written:

```csharp
        var extras = new List<string>();
        foreach (var folder in provider.GetExtraBackupFolders())
        {
            await BackupService.BackupAsync(folder.LivePath, Path.Combine(backupRoot, folder.SnapshotPath), ct);
            extras.Add(folder.SnapshotPath);
        }
```

Then write `new FullBackupManifest(CurrentFormat, provider.GameId, languages, extras)`.

Add after `TakeAsync`:

```csharp
    /// <summary>
    /// Adds the folders a backup lacks (issue 198): a backup taken before PoE2's DLC folders were
    /// read has none of them. Nothing could write those files until then, so the live copies are
    /// still the originals. A format-1 backup (no manifest) is left alone: writing a manifest
    /// would change how its single text folder is read. Returns how many folders were added.
    /// </summary>
    public static async Task<int> TopUpAsync(IGameDataProvider provider, string backupRoot, CancellationToken ct)
    {
        if (ReadManifest(backupRoot) is not { } manifest) return 0;
        var have = (manifest.ExtraFolders ?? []).ToHashSet(StringComparer.Ordinal);
        var missing = provider.GetExtraBackupFolders().Where(f => !have.Contains(f.SnapshotPath)).ToList();
        if (missing.Count == 0) return 0;

        foreach (var folder in missing)
            await BackupService.BackupAsync(folder.LivePath, Path.Combine(backupRoot, folder.SnapshotPath), ct);

        File.WriteAllText(Path.Combine(backupRoot, ManifestFileName), JsonSerializer.Serialize(
            manifest with { Format = Math.Max(manifest.Format, CurrentFormat),
                            ExtraFolders = [.. have.Order(StringComparer.Ordinal), .. missing.Select(f => f.SnapshotPath)] },
            ManifestJson));
        return missing.Count;
    }
```

In `PlanRestore`, inside `if (ReadManifest(backupRoot) is { } manifest)` after the language loop:

```csharp
            var kept = (manifest.ExtraFolders ?? []).ToHashSet(StringComparer.Ordinal);
            foreach (var folder in provider.GetExtraBackupFolders().Where(f => kept.Contains(f.SnapshotPath)))
            {
                var snapshot = Path.Combine(backupRoot, folder.SnapshotPath);
                if (Directory.Exists(snapshot)) pairs.Add(new RestorePair(snapshot, folder.LivePath));
            }
```

- [ ] **Step 5: Implement the top-up on opening a known game folder** (`MainWindowViewModel.cs`)

In `LoadDirectory`, replace

```csharp
            if (!AppSettings.IsKnownGameDirectory(path))
                _ = OfferBackupAsync(path);
```

with

```csharp
            if (!AppSettings.IsKnownGameDirectory(path))
                _ = OfferBackupAsync(path);
            else
                _ = TopUpBackupAsync(path, provider);
```

Add after `RunProviderBackupAsync`:

```csharp
    // A backup taken before issue 198 has none of PoE2's DLC data folders; adding them the next
    // time the game folder is opened still captures the originals (nothing wrote them before).
    private static async Task TopUpBackupAsync(string gameDirectory, IGameDataProvider provider)
    {
        if (FullBackup.Latest(AppSettings.GetBackupPath(gameDirectory) ?? string.Empty) is not { } latest) return;
        try
        {
            var added = await FullBackup.TopUpAsync(provider, latest, default);
            if (added > 0) AppLog.Info($"Backup {latest} topped up with {added} DLC folder(s)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Could not add the DLC folders to the backup {latest}: {ex.Message}");
        }
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&(FullyQualifiedName~FullBackup|FullyQualifiedName~MainWindowViewModelDlcBackupTests|FullyQualifiedName~MainWindowViewModelFullRestoreTests)"`
Expected: PASS, including every existing full-backup and restore test.

- [ ] **Step 7: Run the full suite and commit**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: PASS.

```bash
git add DialogEditor.Core DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/Backup/FullBackupDlcTests.cs DialogEditor.Tests/ViewModels/MainWindowViewModelDlcBackupTests.cs
git commit -m "feat: the full backup keeps PoE2's DLC folders and tops up older backups (#198)"
```

---

### Task 6: The dock groups DLC content by title

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationTreeBuilder.cs`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (after `Browser_Dlc_WhiteMarch2`)
- Test: `DialogEditor.Tests/ViewModels/ConversationTreeBuilderTests.cs` (append)

**Interfaces:**
- Consumes: `ConversationFile.DataRoot` and `BaseDataRoot` (Task 2); `Poe2DataRoots.KeyOf` (Task 1).
- Produces:
  - `ConversationTreeBuilder.GroupPath(string key)` returning `key + "_*"`, so `WhiteMarch1Path == GroupPath("px1")`;
  - the existing `Build(IEnumerable<ConversationFile>)` signature, unchanged.

- [ ] **Step 1: Write the failing tests** (append inside `ConversationTreeBuilderTests`)

```csharp
    // ── PoE2 DLC (issue 198) ─────────────────────────────────────────────────

    private static ConversationFile Dlc(string dataRoot, string folder, string name) =>
        new(name, folder, $@"{folder}\{name}.conversationbundle", "", dataRoot);

    [Fact]
    public void DlcConversations_GoUnderTheirDlcsTitle_InTheTablesOrder()
    {
        var tree = Build(
            File("02_poko_kohara", "a"),
            Dlc("laxi_exported", "laxi", "laxi_00_si_god"),
            Dlc("lax2_exported", "lax02_01_iceberg_dungeon", "lax02_01_cv_ice_carver"),
            Dlc("lax1_exported", "lax01_00_arena_island", "lax01_cv_arena"));

        Assert.Equal(
            ["02_poko_kohara", ConversationTreeBuilder.GroupPath("lax1"), ConversationTreeBuilder.GroupPath("lax2"),
             ConversationTreeBuilder.GroupPath("laxi")],
            tree.Folders.Select(f => f.FolderPath));
        Assert.Equal(["Browser_Dlc_Lax1", "Browser_Dlc_Lax2", "Browser_Dlc_LaxI"], tree.Folders.Skip(1).Select(f => f.DisplayName));
        Assert.Equal("Arena Island", Assert.Single(tree.Folders[1].Subfolders).DisplayName);
        Assert.Equal("Iceberg Dungeon", Assert.Single(tree.Folders[2].Subfolders).DisplayName);
    }

    [Fact]
    public void ABaseGameLaxFolder_JoinsItsDlc_AndMergesWithTheDlcsSameFolder()
    {
        var tree = Build(
            File("lax02_01_iceberg_dungeon", "lax02_01_bs_townie"),                      // base game data
            Dlc("lax2_exported", "lax02_01_iceberg_dungeon", "lax02_01_cv_ice_carver"));

        var group = Assert.Single(tree.Folders);
        Assert.Equal("Browser_Dlc_Lax2", group.DisplayName);
        var iceberg = Assert.Single(group.Subfolders);
        Assert.Equal(["lax02_01_bs_townie", "lax02_01_cv_ice_carver"], iceberg.Items.Select(i => i.Name));
    }

    [Fact]
    public void AFolderNamedJustTheKey_IsTheGroupItself()
    {
        // The base game's "laxi" folder, and laxi_exported's own "laxi" folder: their
        // conversations sit directly in Woedica's Book, not in a "Laxi" subfolder.
        var tree = Build(File("laxi", "laxi_00_bs_book"), Dlc("laxi_exported", "laxi", "laxi_00_si_god"),
                         Dlc("laxi_exported", "", "si_endgame_ultimatechallenge"));

        var group = Assert.Single(tree.Folders);
        Assert.Empty(group.Subfolders);
        Assert.Equal(["laxi_00_bs_book", "laxi_00_si_god", "si_endgame_ultimatechallenge"], group.Items.Select(i => i.Name).Order());
    }

    [Fact]
    public void SameFolderInBaseAndDlc_StaySeparate()   // Review Focus 1
    {
        var tree = Build(File("16_wahaki_island", "base_conv"), Dlc("lax2_exported", "16_wahaki_island", "lax02_16_cv_trader"));

        Assert.Equal("base_conv", Assert.Single(tree.Folders[0].Items).Name);
        Assert.Equal("lax02_16_cv_trader", Assert.Single(Assert.Single(tree.Folders[1].Subfolders).Items).Name);
    }

    [Theory]
    [InlineData("lax02_x", "lax2")]
    [InlineData("lax2_x", "lax2")]
    [InlineData("laxa_x", "laxa")]
    [InlineData("laxi", "laxi")]
    public void BothPrefixSpellings_MapToTheSameDlc(string folder, string key)
        => Assert.Equal(ConversationTreeBuilder.GroupPath(key), Assert.Single(Build(File(folder, "c")).Folders).FolderPath);

    [Theory]
    [InlineData("lax")]          // laxN_exported's own "lax" folder, in the base game: not a key
    [InlineData("laxative")]
    [InlineData("lax10_x")]      // no such DLC
    public void FolderNamesThatOnlyLookLikeAKey_StayWithTheBaseGame(string folder)
        => Assert.Equal(folder, Assert.Single(Build(File(folder, "c")).Folders).FolderPath);

    [Fact]
    public void AnUntitledDataFolder_IsGroupedUnderItsTidiedKey_AfterTheTitledOnes()
    {
        var tree = Build(Dlc("bb_exported", "x", "a"), Dlc("lax3_exported", "x", "b"));
        Assert.Equal(["Browser_Dlc_Lax3", "Bb"], tree.Folders.Select(f => f.DisplayName));
    }

    [Fact]
    public void ThePrototypeGroup_StaysLast()
    {
        var tree = Build(File("00_prototype", "p"), Dlc("lax2_exported", "x", "d"));
        Assert.Equal(ConversationTreeBuilder.PrototypesPath, tree.Folders[^1].FolderPath);
    }
```

The base-game `lax` folder rule: `lax` alone has no digit or letter after it, so it isn't a key. `lax10_x` isn't a known or untitled data folder key, so it stays with the base game.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~ConversationTreeBuilderTests"`
Expected: the build fails, because `GroupPath` doesn't exist.

- [ ] **Step 3: Implement.** Rewrite `ConversationTreeBuilder.Build` and its helpers (keep the class comment; add a bullet for issue 198):

```csharp
    // DLC groups in display order (issue 198 for PoE2). Keys are the data folders' prefixes
    // without "_" (Poe2DataRoots); PoE1's White March folders carry px1_/px2_.
    private static readonly (string Key, string NameKey)[] DlcGroups =
    [
        ("px1", "Browser_Dlc_WhiteMarch1"), ("px2", "Browser_Dlc_WhiteMarch2"),
        ("lax1", "Browser_Dlc_Lax1"), ("lax2", "Browser_Dlc_Lax2"), ("lax3", "Browser_Dlc_Lax3"),
        ("laxa", "Browser_Dlc_LaxA"), ("laxb", "Browser_Dlc_LaxB"), ("laxc", "Browser_Dlc_LaxC"),
        ("laxd", "Browser_Dlc_LaxD"), ("laxe", "Browser_Dlc_LaxE"), ("laxf", "Browser_Dlc_LaxF"),
        ("laxg", "Browser_Dlc_LaxG"), ("laxi", "Browser_Dlc_LaxI"),
    ];

    public static string GroupPath(string key) => key + "_*";
    public static readonly string WhiteMarch1Path = GroupPath("px1");
    public static readonly string WhiteMarch2Path = GroupPath("px2");

    // A base-game folder named for a DLC: px1_…, lax02_… or lax2_… (both spellings occur),
    // laxa_…, or exactly "laxi". Only keys of a real data folder or a titled group count.
    [System.Text.RegularExpressions.GeneratedRegex(@"^(px[12]|lax0?(?<n>\d)|lax(?<l>[a-z]))(_|$)")]
    private static partial System.Text.RegularExpressions.Regex DlcFolderPrefix();

    private static (string Key, string Token)? DlcOf(string topSegment)
    {
        var m = DlcFolderPrefix().Match(topSegment);
        if (!m.Success) return null;
        var key = m.Groups["n"].Success ? "lax" + m.Groups["n"].Value
                : m.Groups["l"].Success ? "lax" + m.Groups["l"].Value
                : m.Groups[1].Value;
        var known = DlcGroups.Any(g => g.Key == key) || Poe2DataRoots.All.Any(r => r.Prefix == key + "_");
        return known ? (key, m.Groups[1].Value) : null;
    }

    private static string GroupName(string key) =>
        DlcGroups.Where(g => g.Key == key).Select(g => Loc.Get(g.NameKey)).FirstOrDefault()
        ?? FolderDisplayName.For(key);

    public static ConversationTree Build(IEnumerable<ConversationFile> files)
    {
        var byId   = new Dictionary<string, ConversationFolderViewModel>(StringComparer.Ordinal);
        var top    = new List<ConversationFolderViewModel>();                                 // base game
        var groups = new Dictionary<string, ConversationFolderViewModel>(StringComparer.Ordinal); // DLC key → group
        var rootItems = new List<ConversationItemViewModel>();

        ConversationFolderViewModel Group(string key)
        {
            if (!groups.TryGetValue(key, out var g))
                groups[key] = g = new ConversationFolderViewModel(GroupPath(key), displayName: GroupName(key));
            return g;
        }

        // A folder is identified by its group and path, so the base game's 16_wahaki_island and
        // Beast of Winter's stay apart, while both games' lax02_01_iceberg_dungeon merge.
        ConversationFolderViewModel FolderFor(string? group, string path)
        {
            var id = (group ?? string.Empty) + "|" + path;
            if (byId.TryGetValue(id, out var existing)) return existing;
            var sep = path.LastIndexOf('\\');
            ConversationFolderViewModel folder;
            if (sep >= 0)
            {
                folder = new ConversationFolderViewModel(path);
                FolderFor(group, path[..sep]).Subfolders.Add(folder);
            }
            else if (group is null)
            {
                folder = new ConversationFolderViewModel(path);
                top.Add(folder);
            }
            else
            {
                // Inside a group the DLC prefix says nothing: px1_00_stalwart_village → "Stalwart Village".
                var token = DlcOf(path)?.Token;
                var shown = token is null ? path : path[token.Length..].TrimStart('_');
                folder = new ConversationFolderViewModel(path, displayName: FolderDisplayName.For(shown));
                Group(group).Subfolders.Add(folder);
            }
            byId[id] = folder;
            return folder;
        }

        foreach (var file in files)
        {
            var path = file.FolderPath.Replace('/', '\\').Trim('\\');
            var item = new ConversationItemViewModel(file);
            var topSegment = path.Split('\\')[0];

            string? group = file.DataRoot != ConversationFile.BaseDataRoot
                ? Poe2DataRoots.KeyOf(file.DataRoot)
                : DlcOf(topSegment)?.Key;

            if (group is null)
            {
                if (path.Length == 0) rootItems.Add(item);
                else FolderFor(null, path).Items.Add(item);
                continue;
            }

            // A top folder named just for the DLC ("laxi") is the group itself.
            if (DlcOf(topSegment) is { } dlc && topSegment == dlc.Token && dlc.Key == group)
                path = path.Length == topSegment.Length ? string.Empty : path[(topSegment.Length + 1)..];

            if (path.Length == 0) Group(group).Items.Add(item);
            else FolderFor(group, path).Items.Add(item);
        }

        var folders = Sorted(top).ToList();
        foreach (var (key, _) in DlcGroups)
            if (groups.TryGetValue(key, out var g)) folders.Add(g);
        foreach (var key in groups.Keys.Where(k => DlcGroups.All(d => d.Key != k)).Order(StringComparer.Ordinal))
            folders.Add(groups[key]);
        foreach (var f in byId.Values.Concat(groups.Values)) SortSubfolders(f);

        // Prototype and test content (part D), from the base game's top level only.
        var leftoverFolders = top.Where(f => IsPrototypeFolder(f.FolderPath)).ToList();
        var leftoverItems = rootItems.Where(i => IsTestConversation(i.Name)).ToList();
        if (leftoverFolders.Count + leftoverItems.Count > 0)
        {
            var group = new ConversationFolderViewModel(PrototypesPath, leftoverItems,
                displayName: Loc.Get("Browser_PrototypesFolder"), toolTip: Loc.Get("ToolTip_PrototypesFolder"));
            foreach (var f in leftoverFolders) { folders.Remove(f); group.Subfolders.Add(f); }
            foreach (var i in leftoverItems) rootItems.Remove(i);
            folders.Add(group);
        }

        return new ConversationTree(folders, rootItems);
    }
```

Delete the old `DlcGroups` tuple (Prefix, Path, NameKey), `DlcGroupOf` and `DlcFolderName`, and the `WhiteMarch1Path`/`WhiteMarch2Path` constants. Keep `PrototypesPath`, `PrototypeFolder`, `TestWord`, `IsPrototypeFolder`, `IsTestConversation`, `Sorted` and `SortSubfolders`. Add `using DialogEditor.Core.GameData;` if it isn't there already. The White March constants change from `const` to `static readonly`, so any `case` label using them must change. `grep -rn WhiteMarch1Path` shows only test uses.

Add to `Strings.axaml` after `Browser_Dlc_WhiteMarch2`:

```xml
    <!-- PoE2's DLC, grouped in the Conversations dock (issue 198). -->
    <sys:String x:Key="Browser_Dlc_Lax1">Seeker, Slayer, Survivor</sys:String>
    <sys:String x:Key="Browser_Dlc_Lax2">Beast of Winter</sys:String>
    <sys:String x:Key="Browser_Dlc_Lax3">The Forgotten Sanctum</sys:String>
    <sys:String x:Key="Browser_Dlc_LaxA">Rum Runner's Pack</sys:String>
    <sys:String x:Key="Browser_Dlc_LaxB">Beard and Hair Pack</sys:String>
    <sys:String x:Key="Browser_Dlc_LaxC">Scalawags Pack</sys:String>
    <sys:String x:Key="Browser_Dlc_LaxD">Deck of Many Things Pack</sys:String>
    <sys:String x:Key="Browser_Dlc_LaxE">Critical Role Pack</sys:String>
    <sys:String x:Key="Browser_Dlc_LaxF">Mega Bosses</sys:String>
    <sys:String x:Key="Browser_Dlc_LaxG">Mega Boss – The Hellfire Colossus</sys:String>
    <sys:String x:Key="Browser_Dlc_LaxI">Woedica's Book</sys:String>
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui&(FullyQualifiedName~ConversationTreeBuilderTests|FullyQualifiedName~GameBrowser)"`
Expected: PASS, including every existing White March and prototype test.

- [ ] **Step 5: Run the full suite and commit**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: PASS.

```bash
git add DialogEditor.ViewModels/ViewModels/ConversationTreeBuilder.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/ViewModels/ConversationTreeBuilderTests.cs
git commit -m "feat: the Conversations dock groups PoE2 DLC content by title (#198)"
```

---

### Task 7: Real-install checks and the README

**Files:**
- Create: `DialogEditor.Tests/GameData/Poe2DlcInstallTests.cs`
- Modify: `README.md`: the PoE2 rows of the supported-builds table, and the **DLC.** paragraph

- [ ] **Step 1: Write the real-install tests**

```csharp
using DialogEditor.Core.GameData;

namespace DialogEditor.Tests.GameData;

/// Issue 198 against a real PoE2 install (DIALOGEDITOR_POE2_DIR; docs/game-data-tests.md).
/// No fixed counts: the expectation is read from the install's own folders.
[Trait("Category", "GameData")]
public class Poe2DlcInstallTests
{
    private static Poe2GameDataProvider Provider() =>
        new(GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe2Variable));

    [GameInstallFact(GameInstallFactAttribute.Poe2Variable)]
    public void EveryDataFolder_IsListed_WithUniqueNames()
    {
        var dir = GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe2Variable);
        var onDisk = Poe2DataRoots.Existing(dir)
            .Select(r => Path.Combine(r.Path, "design", "conversations"))
            .Where(Directory.Exists)
            .Sum(d => Directory.EnumerateFiles(d, "*.conversationbundle", SearchOption.AllDirectories).Count());

        var files = Provider().EnumerateConversations();

        Assert.Equal(onDisk, files.Count);
        Assert.Equal(files.Count, files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [GameInstallFact(GameInstallFactAttribute.Poe2Variable)]
    public void EveryDlcConversation_LoadsWithItsText()
    {
        var provider = Provider();
        var dlc = provider.EnumerateConversations().Where(f => f.DataRoot != ConversationFile.BaseDataRoot).ToList();
        Assert.NotEmpty(dlc);

        var withoutText = dlc
            .Where(f => File.Exists(provider.GetStringTablePath(f)))
            .Where(f => provider.LoadConversation(f).Strings.Entries.Count == 0)
            .Select(f => f.Name).ToList();
        Assert.True(withoutText.Count == 0, "DLC conversations whose text didn't load: " + string.Join(", ", withoutText));
    }
}
```

`ShippedConversationRoundTripTests` already walks `EnumerateConversations()`, so the DLC conversations now get the unchanged-save round trip too, with no new code.

- [ ] **Step 2: Run them against the install**

Run (PowerShell): `$env:DIALOGEDITOR_POE2_DIR='<the PoE2 install>'; dotnet test DialogEditor.Tests --filter "Category=GameData&FullyQualifiedName~Poe2"`
Expected: PASS. `EnumerateConversations` returns 1,403 conversations on a full GOG install. Record the count and the round-trip result in the ledger.

- [ ] **Step 3: Update the README**
  - The PoE2 GOG row's two counts become the count from Step 2, for example `✅ 1,403 conversations, 10 languages`, in both columns.
  - Under the table, after the date line, add: *PoE2's DLC conversations (in the `lax*_exported` data folders) are read since issue [#198](https://github.com/kjmikkel/PillarsDialogEditor/issues/198); the Steam row's DLC count has not been re-checked.*
  - Rewrite the **DLC.** paragraph:

```markdown
**DLC.** Nothing in the editor or patcher depends on which DLC you own. Pillars of Eternity's
White March conversations sit in the normal conversations folder; Deadfire keeps each DLC's data
in its own folder (`lax1_exported`, `lax2_exported`, …), which the editor reads in the game's own
order. The Conversations dock groups DLC content under the DLC's title. An install without a DLC
simply has fewer conversations, and a mod that edits a DLC conversation is skipped on such an
install, with a "conversation not found" warning. Nothing else is written or broken.
```

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Tests/GameData/Poe2DlcInstallTests.cs README.md
git commit -m "test,docs: PoE2 DLC conversations on a real install; README counts (#198)"
```

---

### Task 8: Verification in the app and in the game

This task has no code. It's done after the final review's fixes.

- [ ] **Step 1:** Build, then capture the Conversations dock on PoE2 with every DLC group collapsed, and with Beast of Winter and Woedica's Book expanded. Use the one-off capture used for #85 (`-Expand 'Beast of Winter','Woedica''s Book'`), and look at every capture.
- [ ] **Step 2:** Open `lax02_01_cv_ice_carver` in the editor and check:
  - its text in `en` and one other language;
  - its speaker names;
  - its VO (the Node Details VO status shows Found for a voiced line).
- [ ] **Step 3: In-game (by the user).**
  - Change one Beast of Winter line in a scratch project.
  - Run Test Patch (F5), see the change in game, then Restore (F6).
  - Record the result in the PR.

The editor can't drive the game, so this step is the user's. The PR says it's pending until they report.
