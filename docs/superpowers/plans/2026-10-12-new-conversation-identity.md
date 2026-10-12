# New Conversations the Games Can Start: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A conversation made in the editor and installed (Test Patch, `dialog-patcher`,
Patch Manager) can be started in the game from its first line. In Deadfire it can be found
by name, and removing the mod leaves no trace (GitHub issue 162).

**Architecture:**
- **Identity:** a name-based v5 UUID (`NewConversationId`) gives each new conversation the
  same ID on every install.
- **Template:** the Deadfire new-conversation template carries the ID and every
  conversation-level property a shipped bundle has.
- **Registration:** the Deadfire provider exposes a per-conversation manifest path and
  writer. Both write paths (`PatchInstaller`, Test Patch) back the manifest up and write it
  like any other target, so restore deletes it.
- **Start node:** `NodeIdAllocator`, and the Yarn and Articy importers, start an empty
  conversation at node 0.

**Tech Stack:** C# / .NET 10, System.Text.Json, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-12-new-conversation-identity-design.md`

## Global Constraints

- **Strict red/green TDD:** watch every new test fail for the expected reason before
  writing the implementation (CLAUDE.md).
- **Running tests:** never run bare `dotnet test`; always add `--filter "Category!=Gui"`, or
  a narrower `FullyQualifiedName~…` filter. Build without `-warnaserror` (existing analyzer
  warnings in unrelated files fail it).
- **Issue references in production code** are written `issue 162`, never `#162`.
  `NoStrayHexTests` reads `#` plus three hex digits as a colour literal. Tests may use `#NNN`.
- **Errors:** every caught exception in production code is logged via `AppLog` (CLAUDE.md).
  No new catch blocks are expected.
- **CHANGELOG.md is frozen:** don't touch it.
- **Commit messages** end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **The namespace GUID for `NewConversationId`** is `eed25f96-1230-4e6b-a99f-dd8956046f4f`
  and must never change.
- **Manifest file name:** `dialogeditor_<name>.conversations.manifest`, under
  `PillarsOfEternityII_Data/exported/design/manifests`.
- **Bundle `Filename`:** `Conversations/<name>.conversation`. **Manifest entry `Filename`:**
  `/conversations/<name>.conversationbundle`.

## Review Focus

1. **Mixed-case names** (`Brand_New`): the ID must equal the lower-case name's ID, because
   the game matches names case-insensitively. Pinned in Task 1.
2. **A new conversation the patcher already installed, then Test Patch:** F5 must still
   track and write the manifest, and F6 must put back the patcher's version, not delete it.
   Pinned in Task 5.
3. **Restore where `design/manifests` didn't exist before:** the folder must be removed
   too. Pinned in Task 4.
4. **Two mods in one load order creating the same new conversation:** one manifest file
   with one entry, and the same ID. Pinned in Task 4.
5. **Installing a mod that only edits existing conversations:** no manifest folder or file
   appears. Pinned in Task 4.

---

### Task 1: Name-based conversation ID

**Files:**
- Create: `DialogEditor.Core/GameData/NewConversationId.cs`
- Test: `DialogEditor.Tests/GameData/NewConversationIdTests.cs`

**Interfaces:**
- Produces: `public static Guid NewConversationId.For(string name)` (namespace
  `DialogEditor.Core.GameData`).

- [ ] **Step 1: Write the failing tests**

```csharp
using DialogEditor.Core.GameData;

namespace DialogEditor.Tests.GameData;

/// Issue 162: a new conversation's ID is derived from its name, so every install of it
/// (the patcher rebuilds new conversations from scratch each time) gets the same ID, and
/// save games that refer to it keep working.
public class NewConversationIdTests
{
    [Fact]
    public void SameName_GivesTheSameId() =>
        Assert.Equal(NewConversationId.For("brand_new"), NewConversationId.For("brand_new"));

    // The game looks conversations up by name case-insensitively.
    [Fact]
    public void NamesDifferingOnlyInCase_GiveTheSameId() =>
        Assert.Equal(NewConversationId.For("brand_new"), NewConversationId.For("Brand_New"));

    [Fact]
    public void DifferentNames_GiveDifferentIds() =>
        Assert.NotEqual(NewConversationId.For("brand_new"), NewConversationId.For("brand_old"));

    // RFC 9562: version nibble 5, variant bits 10xx.
    [Fact]
    public void Id_IsAVersion5Uuid()
    {
        var text = NewConversationId.For("brand_new").ToString();
        Assert.Equal('5', text[14]);
        Assert.Contains(text[19], "89ab");
    }

    // Golden values, computed independently with Python's uuid.uuid5 under the editor's
    // namespace. Changing the namespace or the hashing would give every installed new
    // conversation a new ID and break save games that refer to it: that must be deliberate.
    [Theory]
    [InlineData("canonical_mod_new", "78c0ffb3-f8c9-5236-97eb-301ae5903c52")]
    [InlineData("brand_new",         "ab4251f2-bc23-5ddd-921a-af77b515c464")]
    public void Id_IsStableAcrossReleases(string name, string expected) =>
        Assert.Equal(Guid.Parse(expected), NewConversationId.For(name));
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NewConversationIdTests"`
Expected: build error `The name 'NewConversationId' does not exist`. That's a compile
RED. Then add the class with `public static Guid For(string name) => Guid.Empty;` and run
again. Expected: 4 of the 6 cases FAIL (`DifferentNames…`, `Id_IsAVersion5Uuid` and both
golden cases). `SameName_GivesTheSameId` and `NamesDifferingOnlyInCase_GiveTheSameId`
pass trivially on `Guid.Empty`.

- [ ] **Step 3: Implement**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace DialogEditor.Core.GameData;

/// The ID a conversation created in the editor gets in the game (issue 162): an RFC 9562
/// version 5 UUID of its lower-cased name. Deadfire finds a conversation by the ID in its
/// manifest entry and bundle. The patcher rebuilds new conversations on every install, and
/// save games record conversation IDs, so the ID must come out the same every time: derived,
/// never random.
public static class NewConversationId
{
    // NEVER CHANGE: every installed new conversation's ID, and any save game that refers to
    // one, depends on it. NewConversationIdTests pins golden values.
    private static readonly Guid Namespace = new("eed25f96-1230-4e6b-a99f-dd8956046f4f");

    public static Guid For(string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name.ToLowerInvariant());
        var input     = new byte[16 + nameBytes.Length];
        Namespace.TryWriteBytes(input.AsSpan(0, 16), bigEndian: true, out _);
        nameBytes.CopyTo(input, 16);

        var hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);   // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);   // RFC variant
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
```

- [ ] **Step 4: Run them to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NewConversationIdTests"`
Expected: PASS, 6/6. If a golden value differs, the byte order is wrong (both
`TryWriteBytes` and the `Guid` constructor must use `bigEndian: true`). Don't change the
expected values.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/GameData/NewConversationId.cs DialogEditor.Tests/GameData/NewConversationIdTests.cs
git commit -m "feat: name-based IDs for new conversations (#162)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: The Deadfire new-conversation template

**Files:**
- Modify: `DialogEditor.Core/GameData/Poe2GameDataProvider.cs`: replace `Poe2BlankTemplate`
  and `InitializeConversationFile` (currently around lines 234-243).
- Test: `DialogEditor.Tests/GameData/Poe2GameDataProviderTests.cs` (add after
  `InitializeConversationFile_CreatesFileOnDisk`, which uses `_provider` on a temp `_root`).

**Interfaces:**
- Consumes: `NewConversationId.For(string)` (Task 1).
- Produces: `InitializeConversationFile(file)` writes a bundle whose `Conversations[0]` has
  exactly these keys: `ID`, `Filename`, `ConversationScriptNode`, `Nodes`,
  `ConversationType`, `CharacterMappings`, `ClassExtender`, `UObjectName`.

- [ ] **Step 1: Write the failing tests**

Add `using System.Text.Json.Nodes;` to the test file if it isn't there, then add:

```csharp
    // Issue 162: Deadfire loads a conversation by the ID in its manifest entry, and finds
    // its text through the bundle's inner Filename, so a new bundle needs the whole
    // conversation-level shape a shipped one has, not just an empty Nodes list.
    private JsonObject NewTemplate(string name)
    {
        var file = _provider.BuildNewConversationFile(name);
        _provider.InitializeConversationFile(file);
        return JsonNode.Parse(File.ReadAllText(file.ConversationPath))!["Conversations"]![0]!.AsObject();
    }

    [Fact]
    public void InitializeConversationFile_WritesTheConversationLevelProperties()
    {
        var conv = NewTemplate("newconv");

        Assert.Equal(
            ["CharacterMappings", "ClassExtender", "ConversationScriptNode", "ConversationType",
             "Filename", "ID", "Nodes", "UObjectName"],
            conv.Select(kv => kv.Key).Order(StringComparer.Ordinal));
        Assert.Equal(NewConversationId.For("newconv").ToString(), conv["ID"]!.GetValue<string>());
        Assert.Equal("Conversations/newconv.conversation", conv["Filename"]!.GetValue<string>());
        Assert.Equal(-200, conv["ConversationScriptNode"]!["NodeID"]!.GetValue<int>());
        Assert.Equal(0, conv["ConversationType"]!.GetValue<int>());
        Assert.Empty(conv["CharacterMappings"]!.AsArray());
        Assert.Equal("DisplayType,Conversation",
            Assert.Single(conv["ClassExtender"]!["ExtendedProperties"]!.AsArray())!.GetValue<string>());
        Assert.Equal("newconv_Base Game", conv["UObjectName"]!.GetValue<string>());

        var scriptNode = Assert.Single(conv["Nodes"]!.AsArray())!;
        Assert.Equal(-200, scriptNode["NodeID"]!.GetValue<int>());
        Assert.Equal("OEIFormats.FlowCharts.Conversations.ScriptNode, OEIFormats", scriptNode["$type"]!.GetValue<string>());
    }

    [Fact]
    public void InitializeConversationFile_ReadsAsAnEmptyConversation()
    {
        var file = _provider.BuildNewConversationFile("newconv");
        _provider.InitializeConversationFile(file);

        Assert.Empty(_provider.LoadConversation(file).Nodes);   // -200 is never shown
    }

    [Fact]
    public void InitializeConversationFile_SavingNodesKeepsTheIdentity()
    {
        var file = _provider.BuildNewConversationFile("newconv");
        _provider.InitializeConversationFile(file);
        var node0 = new NodeEditSnapshot(0, false, SpeakerCategory.Npc, "", "", "", "",
            "Conversation", "None", "", "", "", false, false, [], [], []);

        _provider.SaveConversation(file, new ConversationEditSnapshot([node0]));

        var conv = JsonNode.Parse(File.ReadAllText(file.ConversationPath))!["Conversations"]![0]!;
        Assert.Equal(NewConversationId.For("newconv").ToString(), conv["ID"]!.GetValue<string>());
        Assert.Equal("Conversations/newconv.conversation", conv["Filename"]!.GetValue<string>());
        Assert.Equal([-200, 0], conv["Nodes"]!.AsArray().Select(n => n!["NodeID"]!.GetValue<int>()));
    }
```

(`NodeEditSnapshot` and `ConversationEditSnapshot` are in `DialogEditor.Core.Editing`, and
`SpeakerCategory` in `DialogEditor.Core.Models`. Add the usings if the file lacks them.)

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe2GameDataProviderTests.InitializeConversationFile"`
Expected:
- `WritesTheConversationLevelProperties` and `SavingNodesKeepsTheIdentity` FAIL: the
  current template has only `Nodes`.
- `ReadsAsAnEmptyConversation` passes already. That's fine: it guards the new template
  against regressing.
- `CreatesFileOnDisk` passes.

- [ ] **Step 3: Implement**

In `Poe2GameDataProvider.cs`, add `using System.Text.Json;` and
`using System.Text.Json.Nodes;` if missing. Replace the `Poe2BlankTemplate` constant and
`InitializeConversationFile` with:

```csharp
    public void InitializeConversationFile(ConversationFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file.ConversationPath)!);
        File.WriteAllText(file.ConversationPath, NewConversationTemplate(file.Name), System.Text.Encoding.UTF8);
    }

    private const string ScriptNodeTypeName = "OEIFormats.FlowCharts.Conversations.ScriptNode, OEIFormats";

    /// A new conversation's bundle, with the conversation-level shape a shipped one has
    /// (issue 162). Deadfire loads a conversation by the ID its manifest entry names, and
    /// finds its text from the inner Filename (the part after "conversations/", as a
    /// .stringtable), so both must be set. The -200 conversation script node appears both
    /// as ConversationScriptNode and in Nodes, as in shipped bundles. The serializer copies
    /// -200 nodes verbatim and the parser never shows them.
    private static string NewConversationTemplate(string name)
    {
        JsonObject ScriptNode() => new()
        {
            ["RequiresValidChildNode"] = false, ["NotSkippable"] = false, ["IsQuestionNode"] = false,
            ["HideSpeaker"] = false, ["IsTempText"] = false, ["PlayVOAs3DSound"] = false,
            ["PlayType"] = 0, ["Persistence"] = 0, ["NoPlayRandomWeight"] = 0, ["VOPositioning"] = 0,
            ["DisplayType"] = 1, ["NodeID"] = -200, ["ContainerNodeID"] = -1,
            ["Links"] = new JsonArray(),
            ["ClassExtender"] = new JsonObject { ["ExtendedProperties"] = new JsonArray() },
            ["Conditionals"] = new JsonObject { ["Operator"] = 0, ["Components"] = new JsonArray() },
            ["OnEnterScripts"] = new JsonArray(), ["OnExitScripts"] = new JsonArray(), ["OnUpdateScripts"] = new JsonArray(),
        };

        var nodeInList = new JsonObject { ["$type"] = ScriptNodeTypeName };
        foreach (var (key, value) in ScriptNode()) nodeInList[key] = value?.DeepClone();

        var conversation = new JsonObject
        {
            ["ID"]                     = NewConversationId.For(name).ToString(),
            ["Filename"]               = $"Conversations/{name}.conversation",
            ["ConversationScriptNode"] = ScriptNode(),
            ["Nodes"]                  = new JsonArray(nodeInList),
            ["ConversationType"]       = 0,
            ["CharacterMappings"]      = new JsonArray(),
            ["ClassExtender"]          = new JsonObject { ["ExtendedProperties"] = new JsonArray("DisplayType,Conversation") },
            ["UObjectName"]            = $"{name}_Base Game",
        };
        return new JsonObject { ["Conversations"] = new JsonArray(conversation) }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
```

- [ ] **Step 4: Run them to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe2GameDataProviderTests|FullyQualifiedName~Poe2ConversationSerializerTests|FullyQualifiedName~PatchInstallerTests|FullyQualifiedName~NewConversationTests"`
Expected: PASS, all.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/GameData/Poe2GameDataProvider.cs DialogEditor.Tests/GameData/Poe2GameDataProviderTests.cs
git commit -m "feat: new Deadfire conversations get an ID and the shipped conversation-level shape (#162)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The conversation manifest on the provider

**Files:**
- Modify: `DialogEditor.Core/GameData/IGameDataProvider.cs`: two default members, after
  `InitializeConversationFile`.
- Modify: `DialogEditor.Core/GameData/Poe2GameDataProvider.cs`: implement both.
- Test: `DialogEditor.Tests/GameData/Poe2GameDataProviderTests.cs` and
  `DialogEditor.Tests/GameData/Poe1GameDataProviderTests.cs` (each has `_provider` on a temp
  `_root`).

**Interfaces:**
- Consumes: `NewConversationId.For(string)` (Task 1).
- Produces:
  - `string? IGameDataProvider.ConversationManifestPath(string name)`: default `null`.
  - `void IGameDataProvider.WriteConversationManifest(string name)`: default does nothing.
  - Deadfire returns `<root>/PillarsOfEternityII_Data/exported/design/manifests/dialogeditor_<name>.conversations.manifest`.

- [ ] **Step 1: Write the failing tests**

In `Poe2GameDataProviderTests.cs`:

```csharp
    // Issue 162: Deadfire only finds conversations listed in a
    // design/manifests/*conversations.manifest, and resolves a name by the file name of
    // each entry's Filename.
    [Fact]
    public void ConversationManifestPath_IsAnEditorOwnedConversationsManifest()
    {
        var path = _provider.ConversationManifestPath("newconv")!;

        Assert.Equal(Path.Combine(_root, "PillarsOfEternityII_Data", "exported", "design", "manifests",
                                  "dialogeditor_newconv.conversations.manifest"), path);
        Assert.EndsWith("conversations.manifest", path);   // the game's *conversations.manifest
    }

    [Fact]
    public void WriteConversationManifest_RegistersTheBundleUnderItsId()
    {
        _provider.WriteConversationManifest("newconv");

        var manifest = JsonNode.Parse(File.ReadAllText(_provider.ConversationManifestPath("newconv")!))!;
        var entry    = Assert.Single(manifest["Entries"]!.AsArray())!;
        Assert.Equal("OEIFormats.Tools.ManifestData+ConversationManifestEntry, OEIFormats", entry["$type"]!.GetValue<string>());
        Assert.Equal(0, entry["DisplayType"]!.GetValue<int>());
        Assert.True(entry["IncludesPlayer"]!.GetValue<bool>());
        Assert.Equal(NewConversationId.For("newconv").ToString(), entry["ID"]!.GetValue<string>());
        Assert.Equal("/conversations/newconv.conversationbundle", entry["Filename"]!.GetValue<string>());
    }

    [Fact]
    public void WriteConversationManifest_IdMatchesTheNewBundle()
    {
        var file = _provider.BuildNewConversationFile("newconv");
        _provider.InitializeConversationFile(file);
        _provider.WriteConversationManifest("newconv");

        var bundleId = JsonNode.Parse(File.ReadAllText(file.ConversationPath))!["Conversations"]![0]!["ID"]!.GetValue<string>();
        var entryId  = JsonNode.Parse(File.ReadAllText(_provider.ConversationManifestPath("newconv")!))!["Entries"]![0]!["ID"]!.GetValue<string>();
        Assert.Equal(bundleId, entryId);
    }
```

In `Poe1GameDataProviderTests.cs`:

```csharp
    // Issue 162: PoE1 finds conversations by path; it has no manifest to register in.
    [Fact]
    public void ConversationManifestPath_IsNull() =>
        Assert.Null(((IGameDataProvider)_provider).ConversationManifestPath("newconv"));
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConversationManifest"`
Expected: build error (`ConversationManifestPath` doesn't exist). Add the two interface
members from Step 3's first block only, then rerun. Expected:
- the three PoE2 tests FAIL (null path / nothing written);
- the PoE1 test passes, since that's the default.

- [ ] **Step 3: Implement**

In `IGameDataProvider.cs`, after `InitializeConversationFile`:

```csharp
    /// Where a conversation created in the editor is registered with the game, or null when
    /// the game finds conversations by path and needs no registration (PoE1). Deadfire
    /// resolves names only through design/manifests/*conversations.manifest (issue 162).
    string? ConversationManifestPath(string name) => null;

    /// Writes the registration at <see cref="ConversationManifestPath"/>. Does nothing when
    /// that is null.
    void WriteConversationManifest(string name) { }
```

In `Poe2GameDataProvider.cs`:

```csharp
    /// One manifest per new conversation, prefixed so it can't collide with a shipped or DLC
    /// manifest, and so a Test Patch of one project can't overwrite another mod's entries.
    /// The game loads every *conversations.manifest (issue 162).
    public string? ConversationManifestPath(string name) =>
        Path.Combine(ExportedRoot, "design", "manifests", $"dialogeditor_{name}.conversations.manifest");

    /// The entry maps the conversation's ID to its bundle, relative to design/. The game
    /// resolves a name by the file name of Filename.
    public void WriteConversationManifest(string name)
    {
        var path  = ConversationManifestPath(name)!;
        var entry = new JsonObject
        {
            ["$type"]          = "OEIFormats.Tools.ManifestData+ConversationManifestEntry, OEIFormats",
            ["DisplayType"]    = 0,
            ["IncludesPlayer"] = true,
            ["ID"]             = NewConversationId.For(name).ToString(),
            ["Filename"]       = $"/conversations/{name}.conversationbundle",
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            new JsonObject { ["Entries"] = new JsonArray(entry) }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            System.Text.Encoding.UTF8);
    }
```

- [ ] **Step 4: Run them to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConversationManifest|FullyQualifiedName~GameDataProviderTests"`
Expected: PASS, all.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/GameData/IGameDataProvider.cs DialogEditor.Core/GameData/Poe2GameDataProvider.cs DialogEditor.Tests/GameData/Poe2GameDataProviderTests.cs DialogEditor.Tests/GameData/Poe1GameDataProviderTests.cs
git commit -m "feat: the Deadfire provider registers new conversations in a manifest (#162)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: The patcher writes and restores the manifest

**Files:**
- Modify: `DialogEditor.Patch/Install/PatchInstaller.cs`: the per-conversation loop in
  `Install` (currently lines 37-56).
- Test: `DialogEditor.Tests/Patch/Install/PatchInstallerTests.cs`.

**Interfaces:**
- Consumes: `IGameDataProvider.ConversationManifestPath(string)`,
  `IGameDataProvider.WriteConversationManifest(string)` (Task 3), and
  `DialogProject.IsNewConversation(string)` (existing).
- Produces: none for later tasks.

- [ ] **Step 1: Write the failing tests**

Add `using System.Text.Json.Nodes;` and `using DialogEditor.Core.GameData;` to the test
file. Then add these to `PatchInstallerTests`. The fake game (`FakePoe2Game`) has no
`design/manifests` folder, which is what the folder-cleanup test relies on.

```csharp
    // ── New conversations are registered with the game (issue #162) ───────

    private static DialogProject NewConversationMod(string modName = "N")
    {
        var node0 = new NodeEditSnapshot(0, false, SpeakerCategory.Npc, "spk", "lst",
            "", "", "Conversation", "None", "", "", "", false, false, [], [], []);
        return DialogProject.Empty(modName)
            .WithNewConversation("brand_new")
            .WithPatch(new ConversationPatch("brand_new", ConversationPatch.CurrentSchemaVersion, [node0], [], []));
    }

    private static string ManifestPath(FakePoe2Game g) => g.Provider.ConversationManifestPath("brand_new")!;

    private static string EntryId(FakePoe2Game g) =>
        Assert.Single(JsonNode.Parse(File.ReadAllText(ManifestPath(g)))!["Entries"]!.AsArray())!["ID"]!.GetValue<string>();

    [Fact]
    public void Install_NewConversation_RegistersItUnderItsBundleId()
    {
        using var game = new FakePoe2Game();
        Install(game, NewConversationMod());

        var bundleId = JsonNode.Parse(File.ReadAllText(game.ConvPath("brand_new")))!["Conversations"]![0]!["ID"]!.GetValue<string>();
        Assert.Equal(bundleId, EntryId(game));
    }

    [Fact]
    public void Restore_RemovesTheManifest_AndTheFolderItCreated()
    {
        using var game = new FakePoe2Game();
        Install(game, NewConversationMod());

        PatchInstaller.Restore(game.Root);

        Assert.False(File.Exists(ManifestPath(game)));
        Assert.False(Directory.Exists(Path.GetDirectoryName(ManifestPath(game))));
    }

    [Fact]
    public void Install_Twice_KeepsTheSameId()
    {
        using var game = new FakePoe2Game();
        Install(game, NewConversationMod());
        var first = EntryId(game);

        Install(game, NewConversationMod());

        Assert.Equal(first, EntryId(game));
    }

    [Fact]
    public void Install_TwoModsCreatingTheSameConversation_RegisterItOnce()
    {
        using var game = new FakePoe2Game();
        Install(game, NewConversationMod("A"), NewConversationMod("B"));

        Assert.Equal(NewConversationId.For("brand_new").ToString(), EntryId(game));
    }

    [Fact]
    public void Install_EditingOnlyExistingConversations_WritesNoManifest()
    {
        using var game = new FakePoe2Game();
        Install(game, FakePoe2Game.ExternalVoMod("A", "a"));

        Assert.False(Directory.Exists(Path.GetDirectoryName(ManifestPath(game))));
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatchInstallerTests"`
Expected:
- `RegistersItUnderItsBundleId`, `Install_Twice_KeepsTheSameId` and
  `TwoModsCreatingTheSameConversation` FAIL (file not found: no manifest written);
- `Restore_RemovesTheManifest…` and `EditingOnlyExistingConversations…` pass already.
  They guard the implementation.

- [ ] **Step 3: Implement**

In `PatchInstaller.Install`, change the loop body from the `targets` line through
`provider.SaveConversation(...)` to:

```csharp
                // A conversation created in the editor must also be registered with the game
                // (issue 162). Keyed on the load order, not on the file being missing: the
                // manifest is a target like any other, so restore removes it.
                var manifest = merged.IsNewConversation(name) ? provider.ConversationManifestPath(name) : null;

                // Back up every target before the first write, so a crash anywhere below
                // leaves a manifest that covers all of them.
                var targets = WithSidecars([file.ConversationPath, .. TranslationApplier.TargetPaths(file, patch, provider)]);
                if (manifest is not null) targets.Add(manifest);
                foreach (var t in targets) store.EnsureBackedUp(t);
                foreach (var t in targets) options.BeforeWrite?.Invoke(t);

                if (!File.Exists(file.ConversationPath)) provider.InitializeConversationFile(file);
                if (manifest is not null) provider.WriteConversationManifest(name);
                var baseSnap = ConversationSnapshotBuilder.Build(provider.LoadConversation(file));
                provider.SaveConversation(file, PatchApplier.Apply(baseSnap, patch, options.Force));
```

(The comment "Back up every target…" exists already; keep one copy. `WithSidecars`
returns `List<string>`, so `Add` works. The manifest gets no `.bak` sidecar, because the
writer doesn't make one.)

- [ ] **Step 4: Run them to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatchInstaller|FullyQualifiedName~PatchCli|FullyQualifiedName~PatchManager"`
Expected: PASS, all.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/Install/PatchInstaller.cs DialogEditor.Tests/Patch/Install/PatchInstallerTests.cs
git commit -m "feat: the patcher registers new conversations and removes them on restore (#162)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Test Patch writes and restores the manifest

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`, in `DoTestPatch`:
  - the first `foreach (var (convName, patch) in _project.Patches)` loop, the one that calls
    `Track` (currently around lines 2270-2291);
  - the second, apply loop (currently around lines 2306-2327).
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelTestPatchTests.cs`.

**Interfaces:**
- Consumes: `IGameDataProvider.ConversationManifestPath(string)` and
  `WriteConversationManifest(string)` (Task 3); the test file's existing `MakeVm`, `Inject`,
  `InjectProject` and `_gameRoot`.
- Produces: none.

- [ ] **Step 1: Write the failing tests**

Add `using System.Text.Json.Nodes;` to the test file if missing, then add:

```csharp
    // ── New conversations are registered with the game (issue #162) ───────

    private static DialogProject NewConversationProject()
    {
        var node0 = new NodeEditSnapshot(0, false, SpeakerCategory.Npc, "spk", "lst",
            "", "", "Conversation", "None", "", "", "", false, false, [], [], []);
        var patch = new ConversationPatch("brand_new", ConversationPatch.CurrentSchemaVersion, [node0], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(0, "first line", "")],
            },
        };
        return DialogProject.Empty("NewConvProj").WithNewConversation("brand_new").WithPatch(patch);
    }

    [Fact]
    public async Task TestPatch_NewConversation_WritesItsManifest_AndRestoreRemovesIt()
    {
        var provider = new Poe2GameDataProvider(_gameRoot);
        var vm       = MakeVm();
        Inject(vm, "_provider", provider);
        InjectProject(vm, NewConversationProject());
        var manifest = provider.ConversationManifestPath("brand_new")!;

        await vm.TestPatchCommand.ExecuteAsync(null);
        Assert.True(File.Exists(manifest), "F5 should register the new conversation");

        vm.RestoreConversationCommand.Execute(null);
        Assert.False(File.Exists(manifest), "F6 should remove the manifest it created");
    }

    // The patcher may already have installed this conversation: its bundle exists, so
    // FindConversation finds it. F5 must still track and write the manifest, and F6 must
    // put the earlier one back rather than delete it.
    [Fact]
    public async Task TestPatch_NewConversationAlreadyInstalled_StillTracksItsManifest()
    {
        var provider = new Poe2GameDataProvider(_gameRoot);
        provider.InitializeConversationFile(provider.BuildNewConversationFile("brand_new"));
        provider.WriteConversationManifest("brand_new");
        var manifest = provider.ConversationManifestPath("brand_new")!;
        File.AppendAllText(manifest, " ");   // mark the pre-existing copy
        var before = File.ReadAllText(manifest);

        var vm = MakeVm();
        Inject(vm, "_provider", provider);
        InjectProject(vm, NewConversationProject());

        await vm.TestPatchCommand.ExecuteAsync(null);
        Assert.NotEqual(before, File.ReadAllText(manifest));   // F5 rewrote it

        vm.RestoreConversationCommand.Execute(null);
        Assert.Equal(before, File.ReadAllText(manifest));
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelTestPatchTests"`
Expected: both new tests FAIL:
- the first, on "F5 should register the new conversation";
- the second, on `Assert.NotEqual`, since F5 didn't touch the manifest.

- [ ] **Step 3: Implement**

In the tracking loop, after `Track(_provider.GetStringTablePath(file));`:

```csharp
                // A conversation created in the editor is also registered with the game
                // (issue 162), keyed on the project rather than on the file being missing:
                // the patcher may already have installed it.
                if (_project.IsNewConversation(convName) && _provider.ConversationManifestPath(convName) is { } manifest)
                    Track(manifest);
```

In the apply loop, right after the `if (!File.Exists(file.ConversationPath))
_provider.InitializeConversationFile(file);` statement:

```csharp
                if (_project.IsNewConversation(convName))
                    _provider.WriteConversationManifest(convName);
```

- [ ] **Step 4: Run them to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelTest"`
Expected: PASS, all. This filter covers the TestPatch, TestPatchPatcher and
TestRestoreSafety classes.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelTestPatchTests.cs
git commit -m "feat: Test Patch registers new conversations and F6 removes them (#162)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: New conversations start at node 0

**Files:**
- Modify: `DialogEditor.Core/Editing/NodeIdAllocator.cs:12`.
- Modify: `DialogEditor.Core/Import/YarnSpinnerImporter.cs:23` (`int nextId = 1;`).
- Modify: `DialogEditor.Core/Import/ArticyXmlImporter.cs:108` (`map[ids[i]] = i + 1;`).
- Test: `DialogEditor.Tests/Editing/NodeIdAllocatorTests.cs`,
  `DialogEditor.Tests/Import/YarnSpinnerImporterTests.cs`,
  `DialogEditor.Tests/Import/ArticyXmlImporterTests.cs`.

**Interfaces:**
- Produces: `NodeIdAllocator.Next([])` returns `0`. Yarn and Articy imports number their
  nodes from 0.

A probe run while planning showed exactly which existing assertions encode numbering from
1. The steps below rewrite those to the new expectation before the change, so they are
the RED. No other test depends on it, apart from the canonical goldens (Task 7).

- [ ] **Step 1: Change the tests to the new expectation**

`NodeIdAllocatorTests.cs`: replace `Next_EmptyList_ReturnsOne` with:

```csharp
    // Issue 162: both games start a conversation at node 0 unless told otherwise, so a new
    // conversation's first node must be 0.
    [Fact]
    public void Next_EmptyList_ReturnsZero()
    {
        Assert.Equal(0, NodeIdAllocator.Next([]));
    }
```

`YarnSpinnerImporterTests.cs` (issue 162: an imported conversation must start at node 0):
- line 43: `Assert.Equal(1, node.NodeId);` becomes `Assert.Equal(0, node.NodeId);`
- lines 73-74: `l.ToNodeId == 2` becomes `l.ToNodeId == 1`, and `l.ToNodeId == 3` becomes
  `l.ToNodeId == 2`.
- line 105: `Assert.Equal(1, node1.NodeId);` becomes `Assert.Equal(0, node1.NodeId);`
- line 108: `Assert.Equal(2, node1.Links[0].ToNodeId);` becomes `Assert.Equal(1, node1.Links[0].ToNodeId);`
- line 111: `Assert.Equal(2, node2.NodeId);` becomes `Assert.Equal(1, node2.NodeId);`
- lines 140-142 comment: renumber to "nodes 0 (NPC), 1 (choice Yes), 2 (choice No)",
  "WorkNode block: node 3" and "EndNode block: node 4".
- line 147: `Assert.Equal(4, choiceYes.Links[0].ToNodeId);` becomes `Assert.Equal(3, …);`
- line 151: `Assert.Equal(5, choiceNo.Links[0].ToNodeId);` becomes `Assert.Equal(4, …);`

`ArticyXmlImporterTests.cs`, in `Import_OutgoingConnections_ProducesLinks` (lines 173-177):

```csharp
        // Fragment 0x00020001 maps to int id 0 (lex sort), 0x00020002 to id 1 (issue 162:
        // an imported conversation must start at node 0).
        var node0 = result.Nodes.Single(n => n.NodeId == 0);
        var link = node0.Links.Single();
        Assert.Equal(0, link.FromNodeId);
        Assert.Equal(1, link.ToNodeId);
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NodeIdAllocatorTests|FullyQualifiedName~YarnSpinnerImporterTests|FullyQualifiedName~ArticyXmlImporterTests"`
Expected: FAIL, exactly these six:
- `Next_EmptyList_ReturnsZero`;
- `Import_SingleNpcLine_ProducesOneNode`;
- `Import_NpcLineFollowedByChoices_ProducesCorrectGraph`;
- `Import_SequentialNpcLines_LinkedInOrder`;
- `Import_ChoiceJump_LinksToFirstNodeOfTargetBlock`;
- `Import_OutgoingConnections_ProducesLinks`.

- [ ] **Step 3: Implement**

- `NodeIdAllocator.cs:12`: `var candidate = ids.Count == 0 ? 0 : ids.Max() + 1;`, and add
  above it: `// An empty conversation starts at node 0, where both games start one (issue 162).`
- `YarnSpinnerImporter.cs:23`: `int nextId = 0;   // the first block's first line is the start node (issue 162)`
- `ArticyXmlImporter.cs:108`: `map[ids[i]] = i;   // from 0, the games' start node (issue 162)`

- [ ] **Step 4: Run them to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: everything passes except
`CanonicalModGoldenTests.CheckedInMod_MatchesAFreshBuild` (poe1 and poe2). The canonical
mods' new conversation now numbers from 0; Task 7 regenerates them. Any *other* failure is
unexpected: stop and debug.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/Editing/NodeIdAllocator.cs DialogEditor.Core/Import/YarnSpinnerImporter.cs DialogEditor.Core/Import/ArticyXmlImporter.cs DialogEditor.Tests/Editing/NodeIdAllocatorTests.cs DialogEditor.Tests/Import/YarnSpinnerImporterTests.cs DialogEditor.Tests/Import/ArticyXmlImporterTests.cs
git commit -m "fix: new and imported conversations start at node 0 (#162)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Canonical mods and the in-game checklist

**Files:**
- Modify: `DialogEditor.Tests/CanonicalMod/CanonicalModRawChecks.cs`: un-skip lines 138 and
  147, and add a manifest check.
- Modify: `DialogEditor.Tests/CanonicalMod/Poe1CanonicalModRawChecks.cs`: un-skip line 83.
- Modify: `DialogEditor.Tests/CanonicalMod/ChecklistTests.cs`: remove the two
  `conversation.new` / `#162` rows from `SkippedElsewhere` (lines 54-55).
- Modify: `docs/canonical-mod-checklist.md`: the two `conversation.new` rows (lines 75 and
  122).
- Regenerate: `DialogEditor.Tests/Fixtures/CanonicalMod/poe1/*` and `poe2/*`.

**Interfaces:**
- Consumes: everything above. `CanonicalGame.Poe2.Provider(root)` returns the Deadfire
  provider for an installed root. `Conversation(root, name)` and `InstalledAsync()` are
  existing helpers in `CanonicalModRawChecks`.

- [ ] **Step 1: Un-skip the checks and add the manifest check**

In `CanonicalModRawChecks.cs`:
- change `[Fact(Skip = "#162: a new PoE2 conversation is written without its
  conversation-level properties")]` to `[Fact]`;
- change `[Fact(Skip = "#162: a new conversation starts at node 1, but the game starts at
  node 0")]` to `[Fact]`;
- add after `NewConversation_StartsAtNode0`:

```csharp
    // Issue #162: Deadfire finds a conversation only through a manifest entry, by the file
    // name of its Filename, and loads it by the entry's ID.
    [Fact]
    public async Task NewConversation_IsRegisteredUnderItsBundleId()
    {
        var root     = await InstalledAsync();
        var path     = CanonicalGame.Poe2.Provider(root).ConversationManifestPath(Poe2Scenario.NewConversation)!;
        var entry    = Assert.Single(JsonNode.Parse(File.ReadAllText(path))!["Entries"]!.AsArray())!;
        Assert.Equal(Conversation(root, Poe2Scenario.NewConversation)["ID"]!.GetValue<string>(), entry["ID"]!.GetValue<string>());
        Assert.Equal(Poe2Scenario.NewConversation, Path.GetFileNameWithoutExtension(entry["Filename"]!.GetValue<string>()));
    }
```

In `Poe1CanonicalModRawChecks.cs`, change
`[Fact(Skip = "#162: a new conversation starts at node 1, but the game starts at node 0")]`
to `[Fact]`.

In `ChecklistTests.cs`, delete these lines:

```csharp
        { "poe1", "conversation.new",     "#162" },
        { "poe2", "conversation.new",     "#162" },
```

- [ ] **Step 2: Rewrite the two checklist rows**

`docs/canonical-mod-checklist.md`, in the Pillars of Eternity section, replace the
`conversation.new` (#162) row with:

```markdown
- [ ] `conversation.new`: `StartConversationFacingListener oei_player canonical_mod_new 0`. The NPC says "Hello from a conversation the mod created.", then two choices. "Second choice, only when cm_flag_a is set." shows only with `cm_flag_a` at 1. Both choices lead to "Goodbye."
```

In the Deadfire section, replace its `conversation.new` (#162) row with:

```markdown
- [ ] `conversation.new`: `StartConversation oei_player canonical_mod_new 0`. Deadfire finds it through the manifest the patcher wrote (`design/manifests/dialogeditor_canonical_mod_new.conversations.manifest`). The NPC says "Hello from a conversation the mod created.", then two choices. "Second choice, only when cm_flag_a is set." shows only with `cm_flag_a` at 1. Both choices lead to "Goodbye."
```

- [ ] **Step 3: Regenerate the goldens and review the diff**

Run (bash): `DIALOGEDITOR_REGENERATE_CANONICAL_MOD=1 dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanonicalModGoldenTests"`
Expected: 2 failures saying "Regenerated … Review the diff". That's by design.

Then: `git diff --ignore-cr-at-eol -- DialogEditor.Tests/Fixtures/CanonicalMod/poe1/canonical_mod.dialogproject DialogEditor.Tests/Fixtures/CanonicalMod/poe2/canonical_mod.dialogproject`
Expected: only `canonical_mod_new`'s node IDs, link endpoints and translation IDs shift
down by one (1-4 become 0-3), along with its layout keys. Nothing in the `canonical`
conversation's patch changes. Any other change is unexpected: stop and investigate.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: PASS. The skipped count drops by 3 from the count before this task. The skips
left are the opt-in game-data tests and the ones that name their open issues.
`RemoveAllMods_RestoresTheFixtureExactly` passing confirms that restore removed the manifest
and the `design/manifests` folder.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Tests/CanonicalMod DialogEditor.Tests/Fixtures/CanonicalMod docs/canonical-mod-checklist.md
git commit -m "test: the canonical mods' new conversations are registered and start at node 0 (#162)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
