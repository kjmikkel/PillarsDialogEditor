# Localization Workflows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable mod authors to store all dialogue text (including their own language) in `ConversationPatch.Translations`, export it for translators, and import translated text back — with writer comments available on any node for translator context.

**Architecture:** Text moves entirely out of `NodeModification.FieldChanges` and `NodeEditSnapshot` serialisation into `ConversationPatch.Translations[langCode]`. `SaveConversation` becomes structure-only; `TranslationApplier` writes all installed-language stringtables. Export/import services handle CSV, JSON, and XLIFF 1.2 files.

**Tech Stack:** C# 12 / .NET 8, xUnit, System.Text.Json, System.Xml.Linq, CommunityToolkit.Mvvm, Avalonia UI

---

## File Map

| Action | File | Purpose |
|--------|------|---------|
| Create | `DialogEditor.Core/Models/NodeTranslation.cs` | Per-node text record |
| Modify | `DialogEditor.Core/Editing/ConversationEditSnapshot.cs` | `[JsonIgnore]` on DefaultText/FemaleText |
| Modify | `DialogEditor.Patch/ConversationPatch.cs` | Add Translations + NodeComments; v2 |
| Modify | `DialogEditor.Patch/DiffEngine.cs` | Remove text from FieldChanges; build Translations |
| Modify | `DialogEditor.Patch/PatchApplier.cs` | Remove DefaultText/FemaleText from ApplyModification |
| Modify | `DialogEditor.Patch/ConversationSnapshotBuilder.cs` | ToConversation accepts translations |
| Modify | `DialogEditor.Core/GameData/IGameDataProvider.cs` | New GetStringTablePath(file, lang) overload |
| Modify | `DialogEditor.Core/GameData/Poe1GameDataProvider.cs` | Implement overload; SaveConversation structure-only |
| Modify | `DialogEditor.Core/GameData/Poe2GameDataProvider.cs` | Same |
| Modify | `DialogEditor.Core/Serialization/StringTableSerializer.cs` | New NodeTranslation overload |
| Create | `DialogEditor.Patch/TranslationApplier.cs` | Write all installed-language stringtables |
| Create | `DialogEditor.Patch/LocalizationExportFormat.cs` | Csv/Json/Xliff enum |
| Create | `DialogEditor.Patch/LocalizationExportService.cs` | CSV, JSON, XLIFF export |
| Create | `DialogEditor.Patch/LocalizationImportService.cs` | CSV, JSON, XLIFF import |
| Modify | `DialogEditor.PatchCli/Program.cs` | Call WriteTranslations after SaveConversation |
| Modify | `DialogEditor.ViewModels/Services/AppSettings.cs` | DefaultLocalizationFormat setting |
| Modify | `DialogEditor.ViewModels/Services/IFilePicker.cs` | Multi-type save overload |
| Modify | `DialogEditor.Avalonia.Shared/Services/AvaloniaFilePicker.cs` | Implement multi-type save |
| Modify | `DialogEditor.Tests/Helpers/StubFilePicker.cs` | Implement multi-type save |
| Modify | `DialogEditor.ViewModels/ViewModels/SettingsViewModel.cs` | LocalizationFormat property |
| Modify | `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` | Export/Import commands; pass language to Diff |
| Modify | `DialogEditor.Avalonia/Resources/Strings.axaml` | New string keys |
| Modify | `DialogEditor.Avalonia/Views/MainWindow.axaml` | Two new File menu items |
| Modify | `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` | Click handlers; LanguageCodeDialog wiring |
| Create | `DialogEditor.Avalonia/Views/LanguageCodeDialog.axaml` | Language-code input modal |
| Create | `DialogEditor.Avalonia/Views/LanguageCodeDialog.axaml.cs` | Code-behind |
| Modify | `DialogEditor.Avalonia/Views/SettingsWindow.axaml` | LocalizationFormat combobox |
| Modify | `DialogEditor.Tests/Patch/PatchSerializerTests.cs` | Update for v2 format |
| Modify | `DialogEditor.Tests/Patch/DiffEngineTests.cs` | Add language param; verify Translations |
| Modify | `DialogEditor.Tests/Patch/LinkConditionTests.cs` | Add language param to Diff calls |
| Modify | `DialogEditor.Tests/Patch/PatchApplierTests.cs` | Remove text-FieldChanges tests |
| Modify | `DialogEditor.Tests/GameData/Poe1GameDataProviderTests.cs` | SaveConversation no longer writes stringtable |
| Modify | `DialogEditor.Tests/GameData/Poe2GameDataProviderTests.cs` | Same |
| Modify | `DialogEditor.Tests/Serialization/StringTableSerializerTests.cs` | Add NodeTranslation overload tests |
| Create | `DialogEditor.Tests/Patch/TranslationApplierTests.cs` | New |
| Create | `DialogEditor.Tests/Patch/LocalizationExportServiceTests.cs` | New |
| Create | `DialogEditor.Tests/Patch/LocalizationImportServiceTests.cs` | New |

---

## Task 1: NodeTranslation record + ConversationPatch schema v2

**Files:**
- Create: `DialogEditor.Core/Models/NodeTranslation.cs`
- Modify: `DialogEditor.Core/Editing/ConversationEditSnapshot.cs`
- Modify: `DialogEditor.Patch/ConversationPatch.cs`
- Test: `DialogEditor.Tests/Patch/PatchSerializerTests.cs`

- [ ] **Step 1: Write failing serialisation tests**

Add to `DialogEditor.Tests/Patch/PatchSerializerTests.cs`:

```csharp
[Fact]
public void Serialize_V2_TranslationsRoundTrip()
{
    var patch = new ConversationPatch("conv", 2, [], [], [])
    {
        Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
        {
            ["en"] = [new NodeTranslation(1, "Hello", "")],
            ["fr"] = [new NodeTranslation(1, "Bonjour", "")],
        }
    };
    var json  = PatchSerializer.Serialize(patch);
    var back  = PatchSerializer.Deserialize(json);
    Assert.Equal(2, back.Translations.Count);
    Assert.Equal("Hello",   back.Translations["en"][0].DefaultText);
    Assert.Equal("Bonjour", back.Translations["fr"][0].DefaultText);
}

[Fact]
public void Serialize_V2_NodeCommentsRoundTrip()
{
    var patch = new ConversationPatch("conv", 2, [], [], [])
    {
        NodeComments = new Dictionary<int, string> { [42] = "Said sarcastically." }
    };
    var back = PatchSerializer.Deserialize(PatchSerializer.Serialize(patch));
    Assert.Equal("Said sarcastically.", back.NodeComments[42]);
}

[Fact]
public void Serialize_V2_AddedNodeHasNoTextInJson()
{
    var node = new NodeEditSnapshot(
        99, false, SpeakerCategory.Npc, "", "", "Hello", "", // DefaultText = "Hello"
        "Conversation", "None", "", "", "", false, false, [], [], []);
    var patch = new ConversationPatch("conv", 2, [node], [], []);
    var json  = PatchSerializer.Serialize(patch);
    // DefaultText must NOT appear in the serialised AddedNodes
    Assert.DoesNotContain("\"DefaultText\"", json.Split("AddedNodes")[1].Split("ModifiedNodes")[0]);
}

[Fact]
public void IsEmpty_TrueWhenOnlyDefaultProperties()
{
    var patch = new ConversationPatch("conv", 2, [], [], []);
    Assert.True(patch.IsEmpty);
}

[Fact]
public void IsEmpty_FalseWhenNodeCommentsPresent()
{
    var patch = new ConversationPatch("conv", 2, [], [], [])
    {
        NodeComments = new Dictionary<int, string> { [1] = "note" }
    };
    Assert.False(patch.IsEmpty);
}

[Fact]
public void IsEmpty_FalseWhenTranslationsPresent()
{
    var patch = new ConversationPatch("conv", 2, [], [], [])
    {
        Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            { ["en"] = [new NodeTranslation(1, "Hi", "")] }
    };
    Assert.False(patch.IsEmpty);
}
```

- [ ] **Step 2: Run tests — expect compile errors (NodeTranslation missing, Translations missing)**

```
dotnet test DialogEditor.Tests --no-build 2>&1 | head -40
```

Expected: build errors about `NodeTranslation` and `Translations`.

- [ ] **Step 3: Create `DialogEditor.Core/Models/NodeTranslation.cs`**

```csharp
namespace DialogEditor.Core.Models;

public record NodeTranslation(int NodeId, string DefaultText, string FemaleText);
```

- [ ] **Step 4: Add `[JsonIgnore]` to DefaultText/FemaleText in `ConversationEditSnapshot.cs`**

Add `using System.Text.Json.Serialization;` at the top. Change the two positional parameters:

```csharp
using System.Text.Json.Serialization;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Editing;

public record LinkEditSnapshot(
    int FromNodeId,
    int ToNodeId,
    float RandomWeight,
    string QuestionNodeTextDisplay,
    bool HasConditions)
{
    public IReadOnlyList<ConditionNode>? Conditions { get; init; }
}

public record NodeEditSnapshot(
    int NodeId,
    bool IsPlayerChoice,
    SpeakerCategory SpeakerCategory,
    string SpeakerGuid,
    string ListenerGuid,
    [property: JsonIgnore] string DefaultText,
    [property: JsonIgnore] string FemaleText,
    string DisplayType,
    string Persistence,
    string ActorDirection,
    string Comments,
    string ExternalVO,
    bool HasVO,
    bool HideSpeaker,
    IReadOnlyList<LinkEditSnapshot> Links,
    IReadOnlyList<ConditionNode> Conditions,
    IReadOnlyList<ScriptCall> Scripts
);

public record ConversationEditSnapshot(IReadOnlyList<NodeEditSnapshot> Nodes);
```

- [ ] **Step 5: Update `DialogEditor.Patch/ConversationPatch.cs`**

```csharp
using System.Text.Json.Serialization;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
#pragma warning disable CS8618

namespace DialogEditor.Patch;

public record FieldChange(string From, string To);
public record DeletedLink(int ToNodeId, bool HasConditions);

public record ModifiedLink(
    int    ToNodeId,
    float  RandomWeight,
    string QuestionNodeTextDisplay,
    IReadOnlyList<ConditionNode>? Conditions = null);

public sealed class NodeModification
{
    [JsonConstructor]
    public NodeModification(
        int                                      nodeId,
        IReadOnlyDictionary<string, FieldChange> fieldChanges,
        IReadOnlyList<LinkEditSnapshot>          addedLinks,
        IReadOnlyList<DeletedLink>               deletedLinks,
        IReadOnlyList<ModifiedLink>              modifiedLinks)
    {
        NodeId        = nodeId;
        FieldChanges  = fieldChanges;
        AddedLinks    = addedLinks;
        DeletedLinks  = deletedLinks;
        ModifiedLinks = modifiedLinks;
    }

    public NodeModification(
        int NodeId,
        IReadOnlyDictionary<string, FieldChange> FieldChanges,
        IReadOnlyList<LinkEditSnapshot> AddedLinks,
        IReadOnlyList<DeletedLink> DeletedLinks)
        : this(NodeId, FieldChanges, AddedLinks, DeletedLinks, []) { }

    public IReadOnlyList<ConditionNode>? UpdatedConditions { get; init; }
    public IReadOnlyList<ScriptCall>?    UpdatedScripts    { get; init; }

    public int                                      NodeId        { get; }
    public IReadOnlyDictionary<string, FieldChange> FieldChanges  { get; }
    public IReadOnlyList<LinkEditSnapshot>          AddedLinks    { get; }
    public IReadOnlyList<DeletedLink>               DeletedLinks  { get; }
    public IReadOnlyList<ModifiedLink>              ModifiedLinks { get; }
}

public record ConversationPatch(
    string                          ConversationName,
    int                             SchemaVersion,
    IReadOnlyList<NodeEditSnapshot> AddedNodes,
    IReadOnlyList<int>              DeletedNodeIds,
    IReadOnlyList<NodeModification> ModifiedNodes)
{
    public static readonly int CurrentSchemaVersion = 2;

    // key = language code e.g. "en", "fr", "de"
    public IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>> Translations { get; init; }
        = new Dictionary<string, IReadOnlyList<NodeTranslation>>();

    // key = node ID; language-neutral translator context
    public IReadOnlyDictionary<int, string> NodeComments { get; init; }
        = new Dictionary<int, string>();

    public bool IsEmpty =>
        AddedNodes.Count    == 0 &&
        DeletedNodeIds.Count == 0 &&
        ModifiedNodes.Count  == 0 &&
        Translations.Count   == 0 &&
        NodeComments.Count   == 0;
}
```

- [ ] **Step 6: Update `PatchSerializerTests` — fix existing round-trip test**

The existing `Serialize_RoundTrip_PreservesAllFields` test checks `node.DefaultText` after deserialisation — it will now be empty because of `[JsonIgnore]`. Remove those two assertions and add a `SchemaVersion` check:

In `MakeRichPatch`, change `ConversationPatch.CurrentSchemaVersion` reference (it is now 2 — confirm the constant still matches).

Remove these two assertions from `Serialize_RoundTrip_PreservesAllFields`:
```csharp
// DELETE these lines:
Assert.Equal("Added text", node.DefaultText);
Assert.Equal("Female text", node.FemaleText);
```

- [ ] **Step 7: Run all tests**

```
dotnet test DialogEditor.Tests -v quiet
```

Expected: all existing tests pass; the 6 new tests pass.

- [ ] **Step 8: Commit**

```
git add DialogEditor.Core/Models/NodeTranslation.cs
git add DialogEditor.Core/Editing/ConversationEditSnapshot.cs
git add DialogEditor.Patch/ConversationPatch.cs
git add DialogEditor.Tests/Patch/PatchSerializerTests.cs
git commit -m "feat: add NodeTranslation; ConversationPatch schema v2 with Translations and NodeComments"
```

---

## Task 2: DiffEngine — text to Translations

**Files:**
- Modify: `DialogEditor.Patch/DiffEngine.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (line 379)
- Modify: `DialogEditor.Tests/Patch/DiffEngineTests.cs`
- Modify: `DialogEditor.Tests/Patch/LinkConditionTests.cs`

- [ ] **Step 1: Write failing tests**

Add to `DialogEditor.Tests/Patch/DiffEngineTests.cs`:

```csharp
// ── Text → Translations ───────────────────────────────────────────────

[Fact]
public void Diff_AddedNode_TextGoesToTranslations()
{
    var baseSnap = Snap();
    var added    = MakeNode(5, defaultText: "Hi", femaleText: "Hello");
    var patch    = DiffEngine.Diff("c", baseSnap, Snap(added), "en");
    Assert.Empty(patch.AddedNodes[0].DefaultText); // not in node
    Assert.True(patch.Translations.ContainsKey("en"));
    var t = patch.Translations["en"].Single(x => x.NodeId == 5);
    Assert.Equal("Hi",    t.DefaultText);
    Assert.Equal("Hello", t.FemaleText);
}

[Fact]
public void Diff_TextChange_GoesToTranslationsNotFieldChanges()
{
    var base_  = MakeNode(1, defaultText: "Old");
    var curr   = MakeNode(1, defaultText: "New");
    var patch  = DiffEngine.Diff("c", Snap(base_), Snap(curr), "fr");
    Assert.False(patch.ModifiedNodes.Any(m => m.FieldChanges.ContainsKey("DefaultText")));
    Assert.True(patch.Translations.ContainsKey("fr"));
    Assert.Equal("New", patch.Translations["fr"].Single(x => x.NodeId == 1).DefaultText);
}

[Fact]
public void Diff_NoTextChange_NoTranslationEntry()
{
    var node  = MakeNode(1, defaultText: "Same");
    var patch = DiffEngine.Diff("c", Snap(node), Snap(node), "en");
    Assert.Empty(patch.Translations);
}

[Fact]
public void Diff_StructuralChangeOnly_NoTranslationEntry()
{
    var base_ = MakeNode(1, defaultText: "Text");
    var curr  = base_ with { IsPlayerChoice = true };
    var patch = DiffEngine.Diff("c", Snap(base_), Snap(curr), "en");
    Assert.Empty(patch.Translations);
}
```

Also update all existing `DiffEngine.Diff(...)` call sites in the test file to add `"en"` as the final argument. Every `DiffEngine.Diff("conv", ...)` call needs `"en"` appended.

- [ ] **Step 2: Run — expect compile errors (wrong number of args)**

```
dotnet build DialogEditor.Tests 2>&1 | grep "error CS"
```

- [ ] **Step 3: Update `DiffEngine.cs`**

```csharp
using System.Text.Json;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

public static class DiffEngine
{
    public static ConversationPatch Diff(
        string conversationName,
        ConversationEditSnapshot baseSnap,
        ConversationEditSnapshot currentSnap,
        string language)
    {
        var baseById    = baseSnap.Nodes.ToDictionary(n => n.NodeId);
        var currentById = currentSnap.Nodes.ToDictionary(n => n.NodeId);

        var added   = currentSnap.Nodes.Where(n => !baseById.ContainsKey(n.NodeId)).ToList();
        var deleted = baseSnap.Nodes.Select(n => n.NodeId)
                              .Where(id => !currentById.ContainsKey(id)).ToList();
        var modified = new List<NodeModification>();

        foreach (var current in currentSnap.Nodes)
        {
            if (!baseById.TryGetValue(current.NodeId, out var @base)) continue;
            var mod = DiffNode(@base, current);
            if (mod is not null) modified.Add(mod);
        }

        // Build Translations[language]: added nodes + nodes with text changes
        var translationList = new List<NodeTranslation>();

        foreach (var node in added)
            translationList.Add(new NodeTranslation(node.NodeId, node.DefaultText, node.FemaleText));

        foreach (var current in currentSnap.Nodes)
        {
            if (!baseById.TryGetValue(current.NodeId, out var @base)) continue;
            if (@base.DefaultText != current.DefaultText || @base.FemaleText != current.FemaleText)
                translationList.Add(new NodeTranslation(current.NodeId, current.DefaultText, current.FemaleText));
        }

        IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>> translations =
            translationList.Count > 0
                ? new Dictionary<string, IReadOnlyList<NodeTranslation>> { [language] = translationList }
                : new Dictionary<string, IReadOnlyList<NodeTranslation>>();

        return new ConversationPatch(
            conversationName,
            ConversationPatch.CurrentSchemaVersion,
            added,
            deleted,
            modified)
            { Translations = translations };
    }

    private static NodeModification? DiffNode(NodeEditSnapshot @base, NodeEditSnapshot current)
    {
        var changes = new Dictionary<string, FieldChange>();

        TryAddChange(changes, "IsPlayerChoice", @base.IsPlayerChoice,    current.IsPlayerChoice);
        TryAddChange(changes, "SpeakerGuid",    @base.SpeakerGuid,       current.SpeakerGuid);
        TryAddChange(changes, "ListenerGuid",   @base.ListenerGuid,      current.ListenerGuid);
        // DefaultText and FemaleText are now in Translations, not FieldChanges
        TryAddChange(changes, "DisplayType",    @base.DisplayType,       current.DisplayType);
        TryAddChange(changes, "Persistence",    @base.Persistence,       current.Persistence);
        TryAddChange(changes, "ActorDirection", @base.ActorDirection,    current.ActorDirection);
        TryAddChange(changes, "Comments",       @base.Comments,          current.Comments);
        TryAddChange(changes, "ExternalVO",     @base.ExternalVO,        current.ExternalVO);
        TryAddChange(changes, "HasVO",          @base.HasVO,             current.HasVO);
        TryAddChange(changes, "HideSpeaker",    @base.HideSpeaker,       current.HideSpeaker);

        var baseLinks    = @base.Links.ToDictionary(l => l.ToNodeId);
        var currentLinks = current.Links.ToDictionary(l => l.ToNodeId);

        var addedLinks   = current.Links.Where(l => !baseLinks.ContainsKey(l.ToNodeId)).ToList();
        var deletedLinks = @base.Links
            .Where(l => !currentLinks.ContainsKey(l.ToNodeId))
            .Select(l => new DeletedLink(l.ToNodeId, l.HasConditions))
            .ToList();
        var modifiedLinks = current.Links
            .Where(l => baseLinks.TryGetValue(l.ToNodeId, out var b) &&
                        (b.RandomWeight != l.RandomWeight ||
                         b.QuestionNodeTextDisplay != l.QuestionNodeTextDisplay ||
                         JsonSerializer.Serialize(b.Conditions) != JsonSerializer.Serialize(l.Conditions)))
            .Select(l =>
            {
                baseLinks.TryGetValue(l.ToNodeId, out var b);
                var condJson = JsonSerializer.Serialize(l.Conditions);
                IReadOnlyList<ConditionNode>? conds =
                    JsonSerializer.Serialize(b?.Conditions) != condJson ? l.Conditions : null;
                return new ModifiedLink(l.ToNodeId, l.RandomWeight, l.QuestionNodeTextDisplay, conds);
            })
            .ToList();

        var baseCondJson    = JsonSerializer.Serialize(@base.Conditions);
        var currentCondJson = JsonSerializer.Serialize(current.Conditions);
        IReadOnlyList<ConditionNode>? updatedConditions =
            baseCondJson != currentCondJson ? current.Conditions : null;

        var baseScriptJson    = JsonSerializer.Serialize(@base.Scripts);
        var currentScriptJson = JsonSerializer.Serialize(current.Scripts);
        IReadOnlyList<ScriptCall>? updatedScripts =
            baseScriptJson != currentScriptJson ? current.Scripts : null;

        if (changes.Count == 0 && addedLinks.Count == 0 && deletedLinks.Count == 0
            && modifiedLinks.Count == 0 && updatedConditions is null && updatedScripts is null)
            return null;

        return new NodeModification(current.NodeId, changes, addedLinks, deletedLinks, modifiedLinks)
            { UpdatedConditions = updatedConditions, UpdatedScripts = updatedScripts };
    }

    private static void TryAddChange<T>(
        Dictionary<string, FieldChange> changes,
        string fieldName,
        T baseValue,
        T currentValue)
    {
        var fromJson = JsonSerializer.Serialize(baseValue);
        var toJson   = JsonSerializer.Serialize(currentValue);
        if (fromJson != toJson)
            changes[fieldName] = new FieldChange(fromJson, toJson);
    }
}
```

- [ ] **Step 4: Update `MainWindowViewModel.cs` line 379**

Change:
```csharp
var patch = DiffEngine.Diff(_currentFile.Name, Canvas.BaseSnapshot, Canvas.BuildSnapshot());
```
To:
```csharp
var patch = DiffEngine.Diff(_currentFile.Name, Canvas.BaseSnapshot, Canvas.BuildSnapshot(), _provider!.Language);
```

- [ ] **Step 5: Update `LinkConditionTests.cs` — add "en" to all Diff calls**

Find all `DiffEngine.Diff("c",` in that file and add `"en"` as the 4th argument.

- [ ] **Step 6: Run all tests**

```
dotnet test DialogEditor.Tests -v quiet
```

Expected: all pass. Remove or update any `DiffEngineTests` that previously tested DefaultText in `FieldChanges` — those tests no longer apply (DefaultText never appears in FieldChanges).

- [ ] **Step 7: Commit**

```
git add DialogEditor.Patch/DiffEngine.cs
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git add DialogEditor.Tests/Patch/DiffEngineTests.cs
git add DialogEditor.Tests/Patch/LinkConditionTests.cs
git commit -m "feat: DiffEngine routes text changes to Translations instead of FieldChanges"
```

---

## Task 3: PatchApplier — remove text field handling

**Files:**
- Modify: `DialogEditor.Patch/PatchApplier.cs`
- Modify: `DialogEditor.Tests/Patch/PatchApplierTests.cs`

- [ ] **Step 1: Write failing test**

Add to `DialogEditor.Tests/Patch/PatchApplierTests.cs`:

```csharp
[Fact]
public void Apply_TextFieldInFieldChanges_ThrowsUnknownField()
{
    // v2 patches must not contain DefaultText in FieldChanges
    var mod = new NodeModification(1,
        new Dictionary<string, FieldChange> { ["DefaultText"] = new("\"old\"", "\"new\"") },
        [], []);
    var snap  = Snap(MakeNode(1));
    var patch = new ConversationPatch("conv", 2, [], [], [mod]);
    Assert.Throws<InvalidOperationException>(() => PatchApplier.Apply(snap, patch));
}
```

- [ ] **Step 2: Run — expect FAIL (currently DefaultText is handled, not thrown)**

```
dotnet test DialogEditor.Tests/Patch/PatchApplierTests.cs -v quiet
```

- [ ] **Step 3: Update `PatchApplier.cs` — remove DefaultText/FemaleText from `ApplyModification`**

In `ApplyModification`, remove:
- The `var defaultText = node.DefaultText;` local variable
- The `var femaleText  = node.FemaleText;` local variable
- The `"DefaultText"` and `"FemaleText"` cases in the `actualJson` switch (the switch now throws `InvalidOperationException` for those keys, which falls through to the default `throw`)
- The `"DefaultText"` and `"FemaleText"` setter cases in the second switch
- `DefaultText = defaultText,` and `FemaleText = femaleText,` from the `return node with { ... }` expression

The method now reads:
```csharp
private static NodeEditSnapshot ApplyModification(
    NodeEditSnapshot node,
    NodeModification mod,
    bool ignoreConflicts = false)
{
    var isPlayerChoice = node.IsPlayerChoice;
    var speakerGuid    = node.SpeakerGuid;
    var listenerGuid   = node.ListenerGuid;
    var displayType    = node.DisplayType;
    var persistence    = node.Persistence;
    var actorDirection = node.ActorDirection;
    var comments       = node.Comments;
    var externalVO     = node.ExternalVO;
    var hasVO          = node.HasVO;
    var hideSpeaker    = node.HideSpeaker;

    foreach (var (field, change) in mod.FieldChanges)
    {
        var actualJson = field switch
        {
            "IsPlayerChoice" => JsonSerializer.Serialize(isPlayerChoice),
            "SpeakerGuid"    => JsonSerializer.Serialize(speakerGuid),
            "ListenerGuid"   => JsonSerializer.Serialize(listenerGuid),
            "DisplayType"    => JsonSerializer.Serialize(displayType),
            "Persistence"    => JsonSerializer.Serialize(persistence),
            "ActorDirection" => JsonSerializer.Serialize(actorDirection),
            "Comments"       => JsonSerializer.Serialize(comments),
            "ExternalVO"     => JsonSerializer.Serialize(externalVO),
            "HasVO"          => JsonSerializer.Serialize(hasVO),
            "HideSpeaker"    => JsonSerializer.Serialize(hideSpeaker),
            _ => throw new InvalidOperationException($"Unknown field: {field}")
        };

        if (actualJson != change.From && !ignoreConflicts)
            throw new PatchConflictException(node.NodeId, field, change.From, actualJson);

        switch (field)
        {
            case "IsPlayerChoice": isPlayerChoice = JsonSerializer.Deserialize<bool>(change.To); break;
            case "SpeakerGuid":    speakerGuid    = JsonSerializer.Deserialize<string>(change.To)!; break;
            case "ListenerGuid":   listenerGuid   = JsonSerializer.Deserialize<string>(change.To)!; break;
            case "DisplayType":    displayType    = JsonSerializer.Deserialize<string>(change.To)!; break;
            case "Persistence":    persistence    = JsonSerializer.Deserialize<string>(change.To)!; break;
            case "ActorDirection": actorDirection = JsonSerializer.Deserialize<string>(change.To)!; break;
            case "Comments":       comments       = JsonSerializer.Deserialize<string>(change.To)!; break;
            case "ExternalVO":     externalVO     = JsonSerializer.Deserialize<string>(change.To)!; break;
            case "HasVO":          hasVO          = JsonSerializer.Deserialize<bool>(change.To); break;
            case "HideSpeaker":    hideSpeaker    = JsonSerializer.Deserialize<bool>(change.To); break;
        }
    }

    var deletedToIds = mod.DeletedLinks.Select(d => d.ToNodeId).ToHashSet();
    var modifiedById = mod.ModifiedLinks.ToDictionary(m => m.ToNodeId);
    var links = node.Links
        .Where(l => !deletedToIds.Contains(l.ToNodeId))
        .Select(l => modifiedById.TryGetValue(l.ToNodeId, out var m)
            ? l with
            {
                RandomWeight            = m.RandomWeight,
                QuestionNodeTextDisplay = m.QuestionNodeTextDisplay,
                Conditions              = m.Conditions ?? l.Conditions,
            }
            : l)
        .Concat(mod.AddedLinks)
        .ToList();

    var conditions = mod.UpdatedConditions ?? node.Conditions;
    var scripts    = mod.UpdatedScripts    ?? node.Scripts;

    return node with
    {
        IsPlayerChoice = isPlayerChoice,
        SpeakerGuid    = speakerGuid,
        ListenerGuid   = listenerGuid,
        DisplayType    = displayType,
        Persistence    = persistence,
        ActorDirection = actorDirection,
        Comments       = comments,
        ExternalVO     = externalVO,
        HasVO          = hasVO,
        HideSpeaker    = hideSpeaker,
        Links          = links,
        Conditions     = conditions,
        Scripts        = scripts,
    };
}
```

- [ ] **Step 4: Remove or update test that put DefaultText in FieldChanges**

In `PatchApplierTests.cs`, find any test that creates a `NodeModification` with `FieldChanges["DefaultText"]`. Delete or replace those tests (they tested behaviour that no longer exists).

- [ ] **Step 5: Run all tests**

```
dotnet test DialogEditor.Tests -v quiet
```

Expected: all pass including the new `Apply_TextFieldInFieldChanges_ThrowsUnknownField` test.

- [ ] **Step 6: Commit**

```
git add DialogEditor.Patch/PatchApplier.cs
git add DialogEditor.Tests/Patch/PatchApplierTests.cs
git commit -m "feat: PatchApplier structure-only — DefaultText/FemaleText removed from FieldChanges handling"
```

---

## Task 4: `GetStringTablePath(file, language)` + structure-only `SaveConversation`

**Files:**
- Modify: `DialogEditor.Core/GameData/IGameDataProvider.cs`
- Modify: `DialogEditor.Core/GameData/Poe1GameDataProvider.cs`
- Modify: `DialogEditor.Core/GameData/Poe2GameDataProvider.cs`
- Modify: `DialogEditor.Tests/GameData/Poe1GameDataProviderTests.cs`
- Modify: `DialogEditor.Tests/GameData/Poe2GameDataProviderTests.cs`

- [ ] **Step 1: Write failing tests**

Add to `DialogEditor.Tests/GameData/Poe1GameDataProviderTests.cs`:

```csharp
[Fact]
public void GetStringTablePath_WithLanguage_ReturnsCorrectPath()
{
    var file = provider.EnumerateConversations().First();
    var enPath = provider.GetStringTablePath(file);
    var frPath = provider.GetStringTablePath(file, "fr");
    Assert.Contains(Path.Combine("localized", "fr"), frPath);
    Assert.Contains(Path.Combine("localized", "en"), enPath);
    Assert.Equal(Path.GetFileName(enPath), Path.GetFileName(frPath));
}

[Fact]
public void SaveConversation_DoesNotWriteStringtable()
{
    var file  = provider.EnumerateConversations().First();
    var conv  = provider.LoadConversation(file);
    var snap  = ConversationSnapshotBuilder.Build(conv);
    var stPath = provider.GetStringTablePath(file);
    var stBefore = File.Exists(stPath) ? File.ReadAllText(stPath) : null;
    provider.SaveConversation(file, snap);
    var stAfter = File.Exists(stPath) ? File.ReadAllText(stPath) : null;
    Assert.Equal(stBefore, stAfter);
}
```

Add the same two tests to `DialogEditor.Tests/GameData/Poe2GameDataProviderTests.cs` (using the poe2 provider fixture).

Also update the existing `SaveConversation_WritesStringtable` test in both files — it should now be removed or renamed to reflect that SaveConversation no longer writes the stringtable. Delete those tests.

- [ ] **Step 2: Run — expect compile errors and/or FAIL**

```
dotnet test DialogEditor.Tests/GameData -v quiet
```

- [ ] **Step 3: Update `IGameDataProvider.cs`**

Add the new overload after the existing one:

```csharp
string GetStringTablePath(ConversationFile file);
string GetStringTablePath(ConversationFile file, string language);
```

- [ ] **Step 4: Update `Poe1GameDataProvider.cs`**

Add the language overload and remove the stringtable write from `SaveConversation`:

```csharp
public string GetStringTablePath(ConversationFile file)
    => StringTablePathFor(file.ConversationPath, Language);

public string GetStringTablePath(ConversationFile file, string language)
    => StringTablePathFor(file.ConversationPath, language);

private string StringTablePathFor(string convPath, string language)
{
    var relative   = Path.GetRelativePath(ConversationsRoot, convPath);
    var withoutExt = Path.ChangeExtension(relative, null);
    var stRoot     = Path.Combine(LocalizedRoot, language, "text", "conversations");
    return Path.Combine(stRoot, withoutExt + ".stringtable");
}

public void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot)
{
    Poe1ConversationSerializer.SaveToFile(file.ConversationPath, snapshot);
    // Stringtable is written by TranslationApplier, not here
}
```

Note: the private `StringTablesRoot` property is no longer used by `SaveConversation`. Update it to use the new helper or remove it if no longer referenced:
```csharp
// Remove the property-based StringTablesRoot usage from SaveConversation.
// The private StringTablesRoot property can remain for BuildNewConversationFile.
```

- [ ] **Step 5: Update `Poe2GameDataProvider.cs`** — identical pattern:

```csharp
public string GetStringTablePath(ConversationFile file)
    => StringTablePathFor(file.ConversationPath, Language);

public string GetStringTablePath(ConversationFile file, string language)
    => StringTablePathFor(file.ConversationPath, language);

private string StringTablePathFor(string convPath, string language)
{
    var relative   = Path.GetRelativePath(ConversationsRoot, convPath);
    var withoutExt = Path.ChangeExtension(relative, null);
    var stRoot     = Path.Combine(LocalizedRoot, language, "text", "conversations");
    return Path.Combine(stRoot, withoutExt + ".stringtable");
}

public void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot)
{
    Poe2ConversationSerializer.SaveToFile(file.ConversationPath, snapshot);
    // Stringtable is written by TranslationApplier, not here
}
```

- [ ] **Step 6: Run all tests**

```
dotnet test DialogEditor.Tests -v quiet
```

Expected: all pass.

- [ ] **Step 7: Commit**

```
git add DialogEditor.Core/GameData/IGameDataProvider.cs
git add DialogEditor.Core/GameData/Poe1GameDataProvider.cs
git add DialogEditor.Core/GameData/Poe2GameDataProvider.cs
git add DialogEditor.Tests/GameData/Poe1GameDataProviderTests.cs
git add DialogEditor.Tests/GameData/Poe2GameDataProviderTests.cs
git commit -m "feat: GetStringTablePath(file, lang) overload; SaveConversation writes structure only"
```

---

## Task 5: `StringTableSerializer` — `NodeTranslation` overload

**Files:**
- Modify: `DialogEditor.Core/Serialization/StringTableSerializer.cs`
- Modify: `DialogEditor.Tests/Serialization/StringTableSerializerTests.cs`

- [ ] **Step 1: Write failing tests**

Add to `StringTableSerializerTests.cs`:

```csharp
// ── NodeTranslation overload ─────────────────────────────────────────

[Fact]
public void SaveToFile_NodeTranslation_WritesEntries()
{
    var dir  = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "test.stringtable");
    try
    {
        var translations = new[]
        {
            new NodeTranslation(1, "Bonjour", ""),
            new NodeTranslation(2, "Au revoir", "Adieu"),
        };
        StringTableSerializer.SaveToFile(path, translations);
        var reparsed = StringTableParser.ParseFile(path);
        Assert.Equal("Bonjour",   reparsed.Get(1)!.DefaultText);
        Assert.Equal("Au revoir", reparsed.Get(2)!.DefaultText);
        Assert.Equal("Adieu",     reparsed.Get(2)!.FemaleText);
    }
    finally { Directory.Delete(dir, true); }
}

[Fact]
public void SaveToFile_NodeTranslation_MergesWithExistingEntries()
{
    var dir  = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "test.stringtable");
    try
    {
        // Pre-populate with two entries
        StringTableSerializer.SaveToFile(path,
            new[] { new NodeTranslation(10, "Hello", ""), new NodeTranslation(11, "World", "") });
        // Now update only node 10
        StringTableSerializer.SaveToFile(path, new[] { new NodeTranslation(10, "Hola", "") });
        var reparsed = StringTableParser.ParseFile(path);
        Assert.Equal("Hola",  reparsed.Get(10)!.DefaultText);
        Assert.Equal("World", reparsed.Get(11)!.DefaultText); // preserved
    }
    finally { Directory.Delete(dir, true); }
}
```

Add `using DialogEditor.Core.Models;` to the test file's using directives.

- [ ] **Step 2: Run — expect compile error (no NodeTranslation overload)**

```
dotnet build DialogEditor.Tests 2>&1 | grep "error CS"
```

- [ ] **Step 3: Add overload to `StringTableSerializer.cs`**

```csharp
public static void SaveToFile(string path, IEnumerable<NodeTranslation> translations)
{
    var original = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    if (File.Exists(path))
        File.Copy(path, path + ".bak", overwrite: true);
    File.WriteAllText(path, SerializeTranslations(original, translations), Encoding.UTF8);
}

private static string SerializeTranslations(string originalXml, IEnumerable<NodeTranslation> translations)
{
    XElement entries;
    XDocument doc;

    if (string.IsNullOrWhiteSpace(originalXml))
    {
        entries = new XElement("Entries");
        doc = new XDocument(new XElement("StringTableFile", entries));
    }
    else
    {
        doc     = XDocument.Parse(originalXml);
        entries = doc.Descendants("Entries").First();
    }

    var byId = entries.Elements("Entry")
        .ToDictionary(e => (int)e.Element("ID")!);

    foreach (var t in translations)
    {
        if (byId.TryGetValue(t.NodeId, out var entry))
        {
            entry.Element("DefaultText")!.Value = t.DefaultText;
            entry.Element("FemaleText")!.Value  = t.FemaleText;
        }
        else
        {
            entries.Add(new XElement("Entry",
                new XElement("ID",          t.NodeId),
                new XElement("DefaultText", t.DefaultText),
                new XElement("FemaleText",  t.FemaleText)));
        }
    }

    return doc.ToString(SaveOptions.None);
}
```

Add `using DialogEditor.Core.Models;` to the serializer file.

- [ ] **Step 4: Run all tests**

```
dotnet test DialogEditor.Tests -v quiet
```

- [ ] **Step 5: Commit**

```
git add DialogEditor.Core/Serialization/StringTableSerializer.cs
git add DialogEditor.Tests/Serialization/StringTableSerializerTests.cs
git commit -m "feat: StringTableSerializer.SaveToFile overload for NodeTranslation"
```

---

## Task 6: `TranslationApplier`

**Files:**
- Create: `DialogEditor.Patch/TranslationApplier.cs`
- Create: `DialogEditor.Tests/Patch/TranslationApplierTests.cs`

- [ ] **Step 1: Write failing tests**

Create `DialogEditor.Tests/Patch/TranslationApplierTests.cs`:

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class TranslationApplierTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly StubProvider _provider;

    public TranslationApplierTests()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "localized", "en"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "localized", "fr"));
        _provider = new StubProvider(_tempRoot, ["en", "fr"]);
    }

    public void Dispose() => Directory.Delete(_tempRoot, true);

    private static ConversationFile MakeFile(string name) =>
        new(name, string.Empty, name + ".conversation", string.Empty);

    [Fact]
    public void WriteTranslations_WritesEnAndFr_WhenBothInstalled()
    {
        var file  = MakeFile("intro");
        var patch = new ConversationPatch("intro", 2, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(1, "Hello", "")],
                ["fr"] = [new NodeTranslation(1, "Bonjour", "")],
            }
        };
        TranslationApplier.WriteTranslations(file, patch, _provider);

        var enPath = _provider.GetStringTablePath(file, "en");
        var frPath = _provider.GetStringTablePath(file, "fr");
        Assert.True(File.Exists(enPath));
        Assert.True(File.Exists(frPath));
        Assert.Contains("Hello",   File.ReadAllText(enPath));
        Assert.Contains("Bonjour", File.ReadAllText(frPath));
    }

    [Fact]
    public void WriteTranslations_SkipsUninstalledLanguage()
    {
        var file  = MakeFile("intro");
        var patch = new ConversationPatch("intro", 2, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["de"] = [new NodeTranslation(1, "Hallo", "")],
            }
        };
        TranslationApplier.WriteTranslations(file, patch, _provider);
        var dePath = _provider.GetStringTablePath(file, "de");
        Assert.False(File.Exists(dePath));
    }

    [Fact]
    public void WriteTranslations_EmptyTranslations_WritesNothing()
    {
        var file  = MakeFile("intro");
        var patch = new ConversationPatch("intro", 2, [], [], []);
        TranslationApplier.WriteTranslations(file, patch, _provider);
        Assert.Empty(Directory.GetFiles(_tempRoot, "*.stringtable", SearchOption.AllDirectories));
    }

    [Fact]
    public void WriteTranslations_AuthorLanguageTreatedLikeAny()
    {
        var file  = MakeFile("intro");
        var patch = new ConversationPatch("intro", 2, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(5, "Author text", "")],
            }
        };
        TranslationApplier.WriteTranslations(file, patch, _provider);
        var enPath = _provider.GetStringTablePath(file, "en");
        Assert.True(File.Exists(enPath));
        Assert.Contains("Author text", File.ReadAllText(enPath));
    }

    private sealed class StubProvider(string root, string[] languages) : IGameDataProvider
    {
        public string GameName => "Stub";
        public string GameId   => "stub";
        public string Language { get; set; } = "en";
        public IReadOnlyList<string> AvailableLanguages => languages;

        public string GetStringTablePath(ConversationFile file)
            => GetStringTablePath(file, Language);

        public string GetStringTablePath(ConversationFile file, string language)
            => Path.Combine(root, "localized", language, "text", "conversations",
                            file.Name + ".stringtable");

        public IReadOnlyList<ConversationFile> EnumerateConversations() => [];
        public Conversation LoadConversation(ConversationFile file) => throw new NotImplementedException();
        public IReadOnlyDictionary<string, string> LoadSpeakerNames() => new Dictionary<string, string>();
        public void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot) { }
        public (string, string) GetBackupRoots() => (root, root);
        public ConversationFile BuildNewConversationFile(string name) => throw new NotImplementedException();
        public void InitializeConversationFile(ConversationFile file) { }
    }
}
```

- [ ] **Step 2: Run — expect compile error (TranslationApplier not found)**

```
dotnet build DialogEditor.Tests 2>&1 | grep "error CS"
```

- [ ] **Step 3: Create `DialogEditor.Patch/TranslationApplier.cs`**

```csharp
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Core.Serialization;

namespace DialogEditor.Patch;

public static class TranslationApplier
{
    public static void WriteTranslations(
        ConversationFile file,
        ConversationPatch patch,
        IGameDataProvider provider)
    {
        if (patch.Translations.Count == 0) return;
        var installed = provider.AvailableLanguages.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (lang, translations) in patch.Translations)
        {
            if (!installed.Contains(lang)) continue;
            var stPath = provider.GetStringTablePath(file, lang);
            Directory.CreateDirectory(Path.GetDirectoryName(stPath)!);
            StringTableSerializer.SaveToFile(stPath, translations);
        }
    }
}
```

- [ ] **Step 4: Run all tests**

```
dotnet test DialogEditor.Tests -v quiet
```

- [ ] **Step 5: Commit**

```
git add DialogEditor.Patch/TranslationApplier.cs
git add DialogEditor.Tests/Patch/TranslationApplierTests.cs
git commit -m "feat: TranslationApplier writes installed-language stringtables from patch.Translations"
```

---

## Task 7: `LocalizationExportService`

**Files:**
- Create: `DialogEditor.Patch/LocalizationExportFormat.cs`
- Create: `DialogEditor.Patch/LocalizationExportService.cs`
- Create: `DialogEditor.Tests/Patch/LocalizationExportServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `DialogEditor.Tests/Patch/LocalizationExportServiceTests.cs`:

```csharp
using System.Text.Json;
using System.Xml.Linq;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class LocalizationExportServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public LocalizationExportServiceTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, true);

    private static DialogProject MakeProject()
    {
        var patch = new ConversationPatch("test_conv", 2, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [
                    new NodeTranslation(1, "Hello", ""),
                    new NodeTranslation(2, "Goodbye", "Farewell"),
                ],
            },
            NodeComments = new Dictionary<int, string>
            {
                [1] = "Greeting on entry",
            },
        };
        return DialogProject.Empty("Test").WithPatch(patch);
    }

    // ── CSV ──────────────────────────────────────────────────────────────

    [Fact]
    public void Export_Csv_HeaderRow()
    {
        var path = Path.Combine(_tempDir, "out.csv");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Csv, "en");
        var lines = File.ReadAllLines(path);
        Assert.Equal(
            "ConversationName,NodeId,WriterComment,SourceDefaultText,SourceFemaleText,TranslatedDefaultText,TranslatedFemaleText",
            lines[0]);
    }

    [Fact]
    public void Export_Csv_NodeRowsContainSourceText()
    {
        var path = Path.Combine(_tempDir, "out.csv");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Csv, "en");
        var content = File.ReadAllText(path);
        Assert.Contains("Hello", content);
        Assert.Contains("Goodbye", content);
        Assert.Contains("Farewell", content);
    }

    [Fact]
    public void Export_Csv_WriterCommentIncluded()
    {
        var path = Path.Combine(_tempDir, "out.csv");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Csv, "en");
        var content = File.ReadAllText(path);
        Assert.Contains("Greeting on entry", content);
    }

    [Fact]
    public void Export_Csv_MissingSourceLanguage_ProducesHeaderOnly()
    {
        var path = Path.Combine(_tempDir, "out.csv");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Csv, "fr");
        var lines = File.ReadAllLines(path);
        Assert.Single(lines); // header only
    }

    [Fact]
    public void Export_Csv_StructuralPatchWithNoTranslations_ProducesHeaderOnly()
    {
        var patch   = new ConversationPatch("c", 2, [], [], []);
        var project = DialogProject.Empty("T").WithPatch(patch);
        var path    = Path.Combine(_tempDir, "out.csv");
        LocalizationExportService.Export(project, path, LocalizationExportFormat.Csv, "en");
        var lines = File.ReadAllLines(path);
        Assert.Single(lines);
    }

    // ── JSON ─────────────────────────────────────────────────────────────

    [Fact]
    public void Export_Json_SourceLanguageRecorded()
    {
        var path = Path.Combine(_tempDir, "out.json");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Json, "en");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("en", doc.RootElement.GetProperty("sourceLanguage").GetString());
    }

    [Fact]
    public void Export_Json_EntriesHaveWriterComment()
    {
        var path = Path.Combine(_tempDir, "out.json");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Json, "en");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToList();
        var node1   = entries.First(e => e.GetProperty("nodeId").GetInt32() == 1);
        Assert.Equal("Greeting on entry", node1.GetProperty("writerComment").GetString());
    }

    [Fact]
    public void Export_Json_EmptyCommentWhenNoneSet()
    {
        var path = Path.Combine(_tempDir, "out.json");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Json, "en");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToList();
        var node2   = entries.First(e => e.GetProperty("nodeId").GetInt32() == 2);
        Assert.Equal("", node2.GetProperty("writerComment").GetString());
    }

    // ── XLIFF ────────────────────────────────────────────────────────────

    [Fact]
    public void Export_Xliff_TransUnitsCreated()
    {
        var path = Path.Combine(_tempDir, "out.xlf");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Xliff, "en");
        var doc   = XDocument.Load(path);
        XNamespace ns = "urn:oasis:names:tc:xliff:document:1.2";
        var units = doc.Descendants(ns + "trans-unit").ToList();
        Assert.Equal(2, units.Count);
    }

    [Fact]
    public void Export_Xliff_WriterCommentAsNote()
    {
        var path = Path.Combine(_tempDir, "out.xlf");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Xliff, "en");
        var doc  = XDocument.Load(path);
        XNamespace ns = "urn:oasis:names:tc:xliff:document:1.2";
        var unit = doc.Descendants(ns + "trans-unit")
                      .First(u => u.Attribute("id")!.Value == "node_1");
        var writerNote = unit.Elements(ns + "note")
                             .FirstOrDefault(n => n.Attribute("from")?.Value == "writer");
        Assert.NotNull(writerNote);
        Assert.Equal("Greeting on entry", writerNote.Value);
    }

    [Fact]
    public void Export_Xliff_FemaleTextAsNote()
    {
        var path = Path.Combine(_tempDir, "out.xlf");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Xliff, "en");
        var doc  = XDocument.Load(path);
        XNamespace ns = "urn:oasis:names:tc:xliff:document:1.2";
        var unit = doc.Descendants(ns + "trans-unit")
                      .First(u => u.Attribute("id")!.Value == "node_2");
        var femaleNote = unit.Elements(ns + "note")
                             .FirstOrDefault(n => n.Attribute("from")?.Value == "female");
        Assert.NotNull(femaleNote);
        Assert.Equal("Farewell", femaleNote.Value);
    }
}
```

- [ ] **Step 2: Run — expect compile errors**

```
dotnet build DialogEditor.Tests 2>&1 | grep "error CS"
```

- [ ] **Step 3: Create `DialogEditor.Patch/LocalizationExportFormat.cs`**

```csharp
namespace DialogEditor.Patch;

public enum LocalizationExportFormat { Csv, Json, Xliff }
```

- [ ] **Step 4: Create `DialogEditor.Patch/LocalizationExportService.cs`**

```csharp
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

public static class LocalizationExportService
{
    public static void Export(
        DialogProject project,
        string outputPath,
        LocalizationExportFormat format,
        string sourceLanguage)
    {
        var rows = BuildRows(project, sourceLanguage);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        switch (format)
        {
            case LocalizationExportFormat.Csv:   WriteCsv(outputPath, rows);                     break;
            case LocalizationExportFormat.Json:  WriteJson(outputPath, sourceLanguage, rows);    break;
            case LocalizationExportFormat.Xliff: WriteXliff(outputPath, sourceLanguage, rows);   break;
        }
    }

    private record ExportRow(
        string ConversationName,
        int    NodeId,
        string WriterComment,
        string SourceDefaultText,
        string SourceFemaleText);

    private static List<ExportRow> BuildRows(DialogProject project, string sourceLanguage)
    {
        var rows = new List<ExportRow>();
        foreach (var (_, patch) in project.Patches)
        {
            if (!patch.Translations.TryGetValue(sourceLanguage, out var translations)) continue;
            foreach (var t in translations)
            {
                var comment = patch.NodeComments.TryGetValue(t.NodeId, out var c) ? c : string.Empty;
                rows.Add(new ExportRow(patch.ConversationName, t.NodeId, comment, t.DefaultText, t.FemaleText));
            }
        }
        return rows;
    }

    // ── CSV ──────────────────────────────────────────────────────────────

    private static void WriteCsv(string path, List<ExportRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ConversationName,NodeId,WriterComment,SourceDefaultText,SourceFemaleText,TranslatedDefaultText,TranslatedFemaleText");
        foreach (var r in rows)
        {
            sb.Append(CsvField(r.ConversationName)); sb.Append(',');
            sb.Append(r.NodeId);                     sb.Append(',');
            sb.Append(CsvField(r.WriterComment));    sb.Append(',');
            sb.Append(CsvField(r.SourceDefaultText));sb.Append(',');
            sb.Append(CsvField(r.SourceFemaleText)); sb.Append(',');
            sb.Append(','); // TranslatedDefaultText — empty
            sb.AppendLine();  // TranslatedFemaleText — empty
        }
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string CsvField(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }

    // ── JSON ─────────────────────────────────────────────────────────────

    private static void WriteJson(string path, string sourceLanguage, List<ExportRow> rows)
    {
        var obj = new
        {
            sourceLanguage,
            targetLanguage = string.Empty,
            entries = rows.Select(r => new
            {
                conversation        = r.ConversationName,
                nodeId              = r.NodeId,
                writerComment       = r.WriterComment,
                sourceDefaultText   = r.SourceDefaultText,
                sourceFemaleText    = r.SourceFemaleText,
                translatedDefaultText = string.Empty,
                translatedFemaleText  = string.Empty,
            }),
        };
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    // ── XLIFF 1.2 ─────────────────────────────────────────────────────────

    private static void WriteXliff(string path, string sourceLanguage, List<ExportRow> rows)
    {
        XNamespace ns = "urn:oasis:names:tc:xliff:document:1.2";
        var byConv = rows.GroupBy(r => r.ConversationName);

        var files = byConv.Select(g =>
        {
            var units = g.Select(r =>
            {
                var unit = new XElement(ns + "trans-unit",
                    new XAttribute("id", $"node_{r.NodeId}"),
                    new XElement(ns + "source", r.SourceDefaultText),
                    new XElement(ns + "target", string.Empty));

                if (!string.IsNullOrEmpty(r.SourceFemaleText))
                    unit.Add(new XElement(ns + "note",
                        new XAttribute("from", "female"), r.SourceFemaleText));

                if (!string.IsNullOrEmpty(r.WriterComment))
                    unit.Add(new XElement(ns + "note",
                        new XAttribute("from", "writer"), r.WriterComment));

                return unit;
            });

            return new XElement(ns + "file",
                new XAttribute("source-language", sourceLanguage),
                new XAttribute("target-language", string.Empty),
                new XAttribute("datatype", "plaintext"),
                new XAttribute("original", g.Key),
                new XElement(ns + "body", units));
        });

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "xliff",
                new XAttribute("version", "1.2"),
                files));

        doc.Save(path);
    }
}
```

- [ ] **Step 5: Run all tests**

```
dotnet test DialogEditor.Tests -v quiet
```

- [ ] **Step 6: Commit**

```
git add DialogEditor.Patch/LocalizationExportFormat.cs
git add DialogEditor.Patch/LocalizationExportService.cs
git add DialogEditor.Tests/Patch/LocalizationExportServiceTests.cs
git commit -m "feat: LocalizationExportService — CSV, JSON, XLIFF export with writer comments"
```

---

## Task 8: `LocalizationImportService`

**Files:**
- Create: `DialogEditor.Patch/LocalizationImportService.cs`
- Create: `DialogEditor.Tests/Patch/LocalizationImportServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `DialogEditor.Tests/Patch/LocalizationImportServiceTests.cs`:

```csharp
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class LocalizationImportServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public LocalizationImportServiceTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, true);

    private static DialogProject BaseProject()
    {
        var patch = new ConversationPatch("conv", 2, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(1, "Hello", ""), new NodeTranslation(2, "Bye", "")],
            },
        };
        return DialogProject.Empty("Test").WithPatch(patch);
    }

    private string ExportAndGetPath(LocalizationExportFormat fmt)
    {
        var ext  = fmt == LocalizationExportFormat.Csv  ? ".csv"
                 : fmt == LocalizationExportFormat.Json ? ".json" : ".xlf";
        var path = Path.Combine(_tempDir, "export" + ext);
        LocalizationExportService.Export(BaseProject(), path, fmt, "en");
        return path;
    }

    // ── CSV round-trip ────────────────────────────────────────────────────

    [Fact]
    public void Import_Csv_RoundTrip_StoresTranslations()
    {
        var exportPath = ExportAndGetPath(LocalizationExportFormat.Csv);

        // Manually edit the CSV to fill in translations
        var lines = File.ReadAllLines(exportPath).ToList();
        // Replace empty translated columns in rows 1 and 2
        lines[1] = lines[1].TrimEnd(',') + "Bonjour,";
        lines[2] = lines[2].TrimEnd(',') + "Au revoir,";
        File.WriteAllLines(exportPath, lines);

        var result = LocalizationImportService.Import(BaseProject(), exportPath,
                                                      LocalizationExportFormat.Csv, "fr");
        var frTrans = result.Patches["conv"].Translations["fr"];
        Assert.Equal(2, frTrans.Count);
        Assert.Equal("Bonjour",   frTrans.First(t => t.NodeId == 1).DefaultText);
        Assert.Equal("Au revoir", frTrans.First(t => t.NodeId == 2).DefaultText);
    }

    [Fact]
    public void Import_Csv_EmptyTranslatedText_Excluded()
    {
        var exportPath = ExportAndGetPath(LocalizationExportFormat.Csv);
        // Leave all translated columns empty (default export state)
        var result = LocalizationImportService.Import(BaseProject(), exportPath,
                                                      LocalizationExportFormat.Csv, "fr");
        Assert.False(result.Patches["conv"].Translations.ContainsKey("fr"));
    }

    [Fact]
    public void Import_Csv_UnknownConversation_SilentlyIgnored()
    {
        // Build a CSV with a conversation name not in the project
        var csv = "ConversationName,NodeId,WriterComment,SourceDefaultText,SourceFemaleText,TranslatedDefaultText,TranslatedFemaleText\n"
                + "unknown_conv,1,,Hello,,Bonjour,\n";
        var path = Path.Combine(_tempDir, "unknown.csv");
        File.WriteAllText(path, csv);
        var result = LocalizationImportService.Import(BaseProject(), path,
                                                      LocalizationExportFormat.Csv, "fr");
        Assert.False(result.Patches.ContainsKey("unknown_conv"));
    }

    // ── JSON round-trip ───────────────────────────────────────────────────

    [Fact]
    public void Import_Json_RoundTrip_StoresTranslations()
    {
        var exportPath = ExportAndGetPath(LocalizationExportFormat.Json);
        var json = File.ReadAllText(exportPath)
            .Replace("\"translatedDefaultText\": \"\"", "\"translatedDefaultText\": \"Bonjour\"");
        File.WriteAllText(exportPath, json);

        var result = LocalizationImportService.Import(BaseProject(), exportPath,
                                                      LocalizationExportFormat.Json, "fr");
        var frTrans = result.Patches["conv"].Translations["fr"];
        Assert.Contains(frTrans, t => t.DefaultText == "Bonjour");
    }

    [Fact]
    public void Import_Json_WriterComments_NotImported()
    {
        var exportPath = ExportAndGetPath(LocalizationExportFormat.Json);
        var result = LocalizationImportService.Import(BaseProject(), exportPath,
                                                      LocalizationExportFormat.Json, "fr");
        // NodeComments should be unchanged (import never writes them)
        Assert.Empty(result.Patches["conv"].NodeComments);
    }

    // ── XLIFF round-trip ──────────────────────────────────────────────────

    [Fact]
    public void Import_Xliff_RoundTrip_StoresTranslations()
    {
        var exportPath = ExportAndGetPath(LocalizationExportFormat.Xliff);
        var content = File.ReadAllText(exportPath)
            .Replace("<target />", "<target>Bonjour</target>");
        File.WriteAllText(exportPath, content);

        var result = LocalizationImportService.Import(BaseProject(), exportPath,
                                                      LocalizationExportFormat.Xliff, "fr");
        var frTrans = result.Patches["conv"].Translations["fr"];
        Assert.NotEmpty(frTrans);
    }

    // ── Author's own language ─────────────────────────────────────────────

    [Fact]
    public void Import_AuthorLanguage_WorksLikeAnyOther()
    {
        var exportPath = ExportAndGetPath(LocalizationExportFormat.Csv);
        var lines = File.ReadAllLines(exportPath).ToList();
        lines[1] = lines[1].TrimEnd(',') + "Updated Hello,";
        File.WriteAllLines(exportPath, lines);

        var result = LocalizationImportService.Import(BaseProject(), exportPath,
                                                      LocalizationExportFormat.Csv, "en");
        var enTrans = result.Patches["conv"].Translations["en"];
        Assert.Contains(enTrans, t => t.NodeId == 1 && t.DefaultText == "Updated Hello");
    }
}
```

- [ ] **Step 2: Run — expect compile error (LocalizationImportService missing)**

```
dotnet build DialogEditor.Tests 2>&1 | grep "error CS"
```

- [ ] **Step 3: Create `DialogEditor.Patch/LocalizationImportService.cs`**

```csharp
using System.Text.Json;
using System.Xml.Linq;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

public static class LocalizationImportService
{
    public static DialogProject Import(
        DialogProject project,
        string inputPath,
        LocalizationExportFormat format,
        string language)
    {
        var grouped = format switch
        {
            LocalizationExportFormat.Csv  => ParseCsv(inputPath),
            LocalizationExportFormat.Json => ParseJson(inputPath),
            LocalizationExportFormat.Xliff => ParseXliff(inputPath),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

        var result = project;
        foreach (var (convName, translations) in grouped)
        {
            if (!result.Patches.TryGetValue(convName, out var patch)) continue;
            var nonEmpty = translations
                .Where(t => !string.IsNullOrEmpty(t.DefaultText) || !string.IsNullOrEmpty(t.FemaleText))
                .ToList();
            if (nonEmpty.Count == 0) continue;

            var newTranslations = new Dictionary<string, IReadOnlyList<NodeTranslation>>(patch.Translations)
            {
                [language] = nonEmpty
            };
            var updatedPatch = patch with { Translations = newTranslations };
            result = result.WithPatch(updatedPatch);
        }
        return result;
    }

    // ── CSV ──────────────────────────────────────────────────────────────

    private static Dictionary<string, List<NodeTranslation>> ParseCsv(string path)
    {
        var result = new Dictionary<string, List<NodeTranslation>>();
        var lines  = File.ReadAllLines(path);
        for (int i = 1; i < lines.Length; i++) // skip header
        {
            var fields = SplitCsvLine(lines[i]);
            if (fields.Count < 7) continue;
            var convName     = fields[0];
            if (!int.TryParse(fields[1], out var nodeId)) continue;
            // fields[2] = WriterComment (ignored on import)
            // fields[3] = SourceDefaultText (ignored)
            // fields[4] = SourceFemaleText (ignored)
            var transDefault = fields[5];
            var transFemale  = fields[6];
            if (!result.ContainsKey(convName)) result[convName] = [];
            result[convName].Add(new NodeTranslation(nodeId, transDefault, transFemale));
        }
        return result;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var fields  = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuote = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuote)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                { current.Append('"'); i++; }
                else if (c == '"') inQuote = false;
                else current.Append(c);
            }
            else
            {
                if (c == '"') inQuote = true;
                else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return fields;
    }

    // ── JSON ─────────────────────────────────────────────────────────────

    private static Dictionary<string, List<NodeTranslation>> ParseJson(string path)
    {
        var result = new Dictionary<string, List<NodeTranslation>>();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("entries", out var entries)) return result;
        foreach (var entry in entries.EnumerateArray())
        {
            var convName     = entry.GetProperty("conversation").GetString() ?? string.Empty;
            var nodeId       = entry.GetProperty("nodeId").GetInt32();
            var transDefault = entry.GetProperty("translatedDefaultText").GetString() ?? string.Empty;
            var transFemale  = entry.GetProperty("translatedFemaleText").GetString()  ?? string.Empty;
            if (!result.ContainsKey(convName)) result[convName] = [];
            result[convName].Add(new NodeTranslation(nodeId, transDefault, transFemale));
        }
        return result;
    }

    // ── XLIFF 1.2 ────────────────────────────────────────────────────────

    private static Dictionary<string, List<NodeTranslation>> ParseXliff(string path)
    {
        var result = new Dictionary<string, List<NodeTranslation>>();
        var doc    = XDocument.Load(path);
        XNamespace ns = "urn:oasis:names:tc:xliff:document:1.2";

        foreach (var file in doc.Descendants(ns + "file"))
        {
            var convName = file.Attribute("original")?.Value ?? string.Empty;
            result[convName] = [];
            foreach (var unit in file.Descendants(ns + "trans-unit"))
            {
                var idAttr = unit.Attribute("id")?.Value ?? string.Empty;
                if (!idAttr.StartsWith("node_")) continue;
                if (!int.TryParse(idAttr[5..], out var nodeId)) continue;
                var target = unit.Element(ns + "target")?.Value ?? string.Empty;
                var female = unit.Elements(ns + "note")
                                 .FirstOrDefault(n => n.Attribute("from")?.Value == "female")
                                 ?.Value ?? string.Empty;
                result[convName].Add(new NodeTranslation(nodeId, target, female));
            }
        }
        return result;
    }
}
```

- [ ] **Step 4: Run all tests**

```
dotnet test DialogEditor.Tests -v quiet
```

- [ ] **Step 5: Commit**

```
git add DialogEditor.Patch/LocalizationImportService.cs
git add DialogEditor.Tests/Patch/LocalizationImportServiceTests.cs
git commit -m "feat: LocalizationImportService — CSV, JSON, XLIFF import into patch Translations"
```

---

## Task 9: `ConversationSnapshotBuilder` text restoration + CLI wiring

**Files:**
- Modify: `DialogEditor.Patch/ConversationSnapshotBuilder.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`
- Modify: `DialogEditor.PatchCli/Program.cs`

- [ ] **Step 1: Update `ConversationSnapshotBuilder.ToConversation`**

The method must accept an optional translation list so new-conversation text can be restored from `patch.Translations` when reopening a project.

```csharp
public static Conversation ToConversation(
    string name,
    ConversationEditSnapshot snap,
    IReadOnlyList<NodeTranslation>? translations = null)
{
    var textById = translations?.ToDictionary(t => t.NodeId)
                  ?? new Dictionary<int, NodeTranslation>();

    var nodes = snap.Nodes.Select(n => new ConversationNode(
        NodeId:          n.NodeId,
        IsPlayerChoice:  n.IsPlayerChoice,
        SpeakerCategory: n.SpeakerCategory,
        SpeakerGuid:     n.SpeakerGuid,
        ListenerGuid:    n.ListenerGuid,
        Links:           n.Links.Select(l => new NodeLink(
                             l.FromNodeId, l.ToNodeId,
                             l.Conditions ?? [], l.RandomWeight,
                             l.QuestionNodeTextDisplay)).ToList(),
        Conditions:      n.Conditions,
        Scripts:         n.Scripts,
        DisplayType:     n.DisplayType,
        Persistence:     n.Persistence,
        ActorDirection:  n.ActorDirection,
        Comments:        n.Comments,
        ExternalVO:      n.ExternalVO,
        HasVO:           n.HasVO,
        HideSpeaker:     n.HideSpeaker)).ToList();

    var strings = new StringTable(snap.Nodes.Select(n =>
    {
        textById.TryGetValue(n.NodeId, out var t);
        return new StringEntry(n.NodeId,
            t?.DefaultText ?? n.DefaultText,
            t?.FemaleText  ?? n.FemaleText);
    }).ToList());

    return new Conversation(name, nodes, strings);
}
```

- [ ] **Step 2: Update `MainWindowViewModel.LoadNewConversation`**

At the call site (~line 356), pass translations from the existing patch:

```csharp
if (_project?.Patches.TryGetValue(file.Name, out var existingPatch) == true)
{
    var baseSnap    = new ConversationEditSnapshot([]);
    var appliedSnap = PatchApplier.Apply(baseSnap, existingPatch);
    var translations = existingPatch.Translations
        .GetValueOrDefault(_provider?.Language ?? "en");
    var restored = ConversationSnapshotBuilder.ToConversation(
        file.Name, appliedSnap, translations);
    Canvas.Load(restored);
}
```

- [ ] **Step 3: Update `DialogEditor.PatchCli/Program.cs`**

After line 163 (`provider.SaveConversation(file, result);`), add:

```csharp
provider.SaveConversation(file, result);
TranslationApplier.WriteTranslations(file, patch, provider);
```

Add `using DialogEditor.Patch;` if not already present (it should be).

- [ ] **Step 4: Build and run all tests**

```
dotnet build
dotnet test DialogEditor.Tests -v quiet
```

- [ ] **Step 5: Commit**

```
git add DialogEditor.Patch/ConversationSnapshotBuilder.cs
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git add DialogEditor.PatchCli/Program.cs
git commit -m "feat: ToConversation restores text from Translations; CLI calls TranslationApplier"
```

---

## Task 10: `AppSettings` + `SettingsViewModel`

**Files:**
- Modify: `DialogEditor.ViewModels/Services/AppSettings.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/SettingsViewModel.cs`
- Modify: `DialogEditor.Tests/ViewModels/LowPriorityViewModelTests.cs`

- [ ] **Step 1: Write failing test**

Add to `SettingsViewModelTests` in `LowPriorityViewModelTests.cs`:

```csharp
[Fact]
public void LocalizationFormat_DefaultIsCsv()
{
    var vm = new SettingsViewModel("/game", new StubFolderPicker());
    Assert.Equal("Csv", vm.LocalizationFormat);
}

[Fact]
public void LocalizationFormat_RoundTripsViaAppSettings()
{
    // AppSettings.DefaultLocalizationFormat is static; test the ViewModel binding
    var vm = new SettingsViewModel("/game", new StubFolderPicker());
    vm.LocalizationFormat = "Json";
    Assert.Equal("Json", AppSettings.DefaultLocalizationFormat);
    // Reset
    AppSettings.DefaultLocalizationFormat = "Csv";
}
```

- [ ] **Step 2: Run — expect FAIL (LocalizationFormat not on SettingsViewModel)**

```
dotnet build DialogEditor.Tests 2>&1 | grep "error CS"
```

- [ ] **Step 3: Update `AppSettings.cs`**

Inside `SettingsData` sealed class, add:
```csharp
public string DefaultLocalizationFormat { get; set; } = "Csv";
```

Outside the class, add the static property:
```csharp
public static string DefaultLocalizationFormat
{
    get => Load().DefaultLocalizationFormat;
    set { var s = Load(); s.DefaultLocalizationFormat = value; Save(s); }
}
```

- [ ] **Step 4: Update `SettingsViewModel.cs`**

```csharp
[ObservableProperty] private string _localizationFormat;

public SettingsViewModel(string gameDirectory, IFolderPicker picker)
{
    _gameDirectory     = gameDirectory;
    _picker            = picker;
    _backupDirectory   = AppSettings.GetBackupPath(gameDirectory) ?? string.Empty;
    _localizationFormat = AppSettings.DefaultLocalizationFormat;
}

partial void OnLocalizationFormatChanged(string value)
    => AppSettings.DefaultLocalizationFormat = value;
```

- [ ] **Step 5: Run all tests**

```
dotnet test DialogEditor.Tests -v quiet
```

- [ ] **Step 6: Commit**

```
git add DialogEditor.ViewModels/Services/AppSettings.cs
git add DialogEditor.ViewModels/ViewModels/SettingsViewModel.cs
git add DialogEditor.Tests/ViewModels/LowPriorityViewModelTests.cs
git commit -m "feat: AppSettings.DefaultLocalizationFormat; SettingsViewModel.LocalizationFormat"
```

---

## Task 11: `MainWindowViewModel` export/import commands

**Files:**
- Modify: `DialogEditor.ViewModels/Services/IFilePicker.cs`
- Modify: `DialogEditor.Avalonia.Shared/Services/AvaloniaFilePicker.cs`
- Modify: `DialogEditor.Tests/Helpers/StubFilePicker.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Add multi-type save overload to `IFilePicker`**

```csharp
/// <summary>Returns chosen save path, or null if cancelled. Accepts multiple file type options.</summary>
Task<string?> PickSaveFileAsync(
    string title,
    string suggestedName,
    IReadOnlyList<(string Extension, string Description)> fileTypes);
```

- [ ] **Step 2: Implement in `AvaloniaFilePicker.cs`**

```csharp
public async Task<string?> PickSaveFileAsync(
    string title,
    string suggestedName,
    IReadOnlyList<(string Extension, string Description)> fileTypes)
{
    var result = await topLevel.StorageProvider.SaveFilePickerAsync(
        new FilePickerSaveOptions
        {
            Title             = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices   = fileTypes.Select(ft =>
                new FilePickerFileType(ft.Description)
                    { Patterns = [$"*{ft.Extension}"] }).ToList(),
        });
    return result?.Path.LocalPath;
}
```

- [ ] **Step 3: Implement in `StubFilePicker.cs`**

```csharp
public Task<string?> PickSaveFileAsync(
    string title,
    string suggestedName,
    IReadOnlyList<(string Extension, string Description)> fileTypes)
    => Task.FromResult(_saveResult);
```

- [ ] **Step 4: Add commands to `MainWindowViewModel.cs`**

Add delegate property (set by the UI layer to show the LanguageCodeDialog):
```csharp
/// <summary>Set by UI: shows a language-code dialog pre-populated with initialValue. Returns null on cancel.</summary>
public Func<string, Task<string?>>? RequestLanguageCode { get; set; }
```

Add `CanExecute` helper and two commands:
```csharp
private bool HasProject() => _project is not null;

[RelayCommand(CanExecute = nameof(HasProject))]
private async Task ExportForTranslation()
{
    if (_project is null || _provider is null) return;
    try
    {
        var langCode = await (RequestLanguageCode?.Invoke(_provider.Language)
                       ?? Task.FromResult<string?>(_provider.Language));
        if (langCode is null) return;

        var format = AppSettings.DefaultLocalizationFormat switch
        {
            "Json"  => LocalizationExportFormat.Json,
            "Xliff" => LocalizationExportFormat.Xliff,
            _       => LocalizationExportFormat.Csv,
        };

        var fileTypes = new (string, string)[]
        {
            (".csv",  "CSV Files"),
            (".json", "JSON Files"),
            (".xlf",  "XLIFF Files"),
        };
        var ext  = format == LocalizationExportFormat.Json  ? ".json"
                 : format == LocalizationExportFormat.Xliff ? ".xlf" : ".csv";
        var path = await _filePicker.PickSaveFileAsync(
            Loc.Get("Menu_ExportForTranslation"),
            _project.Name + "_translations" + ext,
            fileTypes);
        if (path is null) return;

        LocalizationExportService.Export(_project, path, format, langCode);
        var count = _project.Patches.Values
            .Sum(p => p.Translations.TryGetValue(langCode, out var t) ? t.Count : 0);
        StatusText = Loc.Format("Localization_StatusExported", count, Path.GetFileName(path));
    }
    catch (Exception ex)
    {
        AppLog.Error("Export for translation failed", ex);
        StatusText = ex.Message;
    }
}

[RelayCommand(CanExecute = nameof(HasProject))]
private async Task ImportTranslation()
{
    if (_project is null) return;
    try
    {
        var path = await _filePicker.PickOpenFileAsync(
            Loc.Get("Menu_ImportTranslation"), ".csv;*.json;*.xlf;*.xliff",
            "Translation Files");
        if (path is null) return;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var format = ext switch
        {
            ".json"  => LocalizationExportFormat.Json,
            ".xlf"   => LocalizationExportFormat.Xliff,
            ".xliff" => LocalizationExportFormat.Xliff,
            _        => LocalizationExportFormat.Csv,
        };

        var defaultLang = string.Empty;
        var langCode = await (RequestLanguageCode?.Invoke(defaultLang)
                       ?? Task.FromResult<string?>(null));
        if (langCode is null) return;

        var updated = LocalizationImportService.Import(_project, path, format, langCode);
        SetProject(updated);
        IsModified = true;

        var count = updated.Patches.Values
            .Sum(p => p.Translations.TryGetValue(langCode, out var t) ? t.Count : 0);
        if (count == 0)
            StatusText = Loc.Get("Localization_StatusImportNoEntries");
        else
            StatusText = Loc.Format("Localization_StatusImported", count, langCode);
    }
    catch (Exception ex)
    {
        AppLog.Error("Import translation failed", ex);
        StatusText = ex.Message;
    }
}
```

Add required usings at the top of MainWindowViewModel.cs:
```csharp
using DialogEditor.Patch;
```

Call `NotifyCanExecuteChanged` when the project opens/closes — find where `HasProject`-gated commands are already notified and add `ExportForTranslationCommand.NotifyCanExecuteChanged()` and `ImportTranslationCommand.NotifyCanExecuteChanged()` in the same places.

- [ ] **Step 5: Build**

```
dotnet build
```

Expected: clean build.

- [ ] **Step 6: Commit**

```
git add DialogEditor.ViewModels/Services/IFilePicker.cs
git add DialogEditor.Avalonia.Shared/Services/AvaloniaFilePicker.cs
git add DialogEditor.Tests/Helpers/StubFilePicker.cs
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git commit -m "feat: ExportForTranslationCommand + ImportTranslationCommand in MainWindowViewModel"
```

---

## Task 12: Avalonia UI

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`
- Create: `DialogEditor.Avalonia/Views/LanguageCodeDialog.axaml`
- Create: `DialogEditor.Avalonia/Views/LanguageCodeDialog.axaml.cs`
- Modify: `DialogEditor.Avalonia/Views/SettingsWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/SettingsWindow.axaml.cs` (if Height needs adjusting)

- [ ] **Step 1: Add string keys to `Strings.axaml`**

Find the end of the `<!-- ─── File menu ───` section (or insert before the closing `</ResourceDictionary>`) and add:

```xml
<!-- ─── Localization ──────────────────────────────────────────────── -->
<sys:String x:Key="Menu_ExportForTranslation">Export for Translation…</sys:String>
<sys:String x:Key="Menu_ImportTranslation">Import Translation…</sys:String>
<!-- Shown in ToolTip on File menu items -->
<sys:String x:Key="ToolTip_ExportForTranslation">Export all patch text to a file for human translation (CSV, JSON, or XLIFF).</sys:String>
<sys:String x:Key="ToolTip_ImportTranslation">Import a translated file and store the translations in the active project.</sys:String>
<!-- Title and prompt for the language-code input dialog -->
<sys:String x:Key="Localization_LanguageCodeDialog_Title">Language Code</sys:String>
<sys:String x:Key="Localization_LanguageCodeDialog_Prompt">Language code (e.g. fr, de, ja):</sys:String>
<!-- {0} = entry count, {1} = file name -->
<sys:String x:Key="Localization_StatusExported">{0} entries exported to {1}</sys:String>
<!-- {0} = entry count, {1} = language code -->
<sys:String x:Key="Localization_StatusImported">{0} translations imported for {1}</sys:String>
<sys:String x:Key="Localization_StatusImportNoEntries">No translated entries found in file</sys:String>
<!-- Label for the localization format setting -->
<sys:String x:Key="Settings_LocalizationFormat">Export format</sys:String>
<sys:String x:Key="ToolTip_LocalizationFormat">Default file format used when exporting text for translation.</sys:String>
```

- [ ] **Step 2: Add menu items to `MainWindow.axaml`**

Inside the `<MenuItem Header="{StaticResource Menu_File}">` block, after the last `<Separator/>` before `Menu_Settings`:

```xml
<Separator/>
<MenuItem Header="{StaticResource Menu_ExportForTranslation}"
          Command="{Binding ExportForTranslationCommand}"
          ToolTip.Tip="{StaticResource ToolTip_ExportForTranslation}"/>
<MenuItem Header="{StaticResource Menu_ImportTranslation}"
          Command="{Binding ImportTranslationCommand}"
          ToolTip.Tip="{StaticResource ToolTip_ImportTranslation}"/>
```

- [ ] **Step 3: Create `LanguageCodeDialog.axaml`**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.LanguageCodeDialog"
        Title="{StaticResource Localization_LanguageCodeDialog_Title}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="340" Height="160"
        CanResize="False"
        Background="#1e1e1e"
        WindowStartupLocation="CenterOwner"
        x:CompileBindings="False">

    <Grid RowDefinitions="*,Auto" Margin="16">

        <StackPanel Grid.Row="0" Spacing="10" VerticalAlignment="Center">
            <TextBlock Text="{StaticResource Localization_LanguageCodeDialog_Prompt}"
                       Foreground="#aaa" FontSize="12"/>
            <TextBox x:Name="LanguageCodeBox"
                     Background="#1a1a1a" Foreground="#e8e8e8"
                     BorderBrush="#444" BorderThickness="1"
                     FontSize="12" Padding="6,4"
                     KeyDown="LanguageCodeBox_KeyDown"/>
        </StackPanel>

        <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
            <Button Content="Cancel"
                    Background="#333" Foreground="#ccc" BorderThickness="0"
                    Padding="16,5"
                    Click="Cancel_Click"/>
            <Button Content="OK"
                    Background="#4a7c4e" Foreground="#fff" BorderThickness="0"
                    Padding="16,5"
                    Click="Ok_Click"/>
        </StackPanel>

    </Grid>
</Window>
```

- [ ] **Step 4: Create `LanguageCodeDialog.axaml.cs`**

```csharp
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DialogEditor.Avalonia.Views;

public partial class LanguageCodeDialog : Window
{
    public string? Result { get; private set; }

    public LanguageCodeDialog(string initialValue = "")
    {
        InitializeComponent();
        LanguageCodeBox.Text = initialValue;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Confirm();
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void LanguageCodeBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  Confirm();
        if (e.Key == Key.Escape) Close();
    }

    private void Confirm()
    {
        var code = LanguageCodeBox.Text?.Trim();
        if (string.IsNullOrEmpty(code)) return;
        Result = code;
        Close();
    }
}
```

- [ ] **Step 5: Wire `RequestLanguageCode` in `MainWindow.axaml.cs`**

In the `MainWindow` constructor, after the existing `vm.RequestConflictResolution = ...` line, add:

```csharp
vm.RequestLanguageCode = initialValue => ShowLanguageCodeDialogAsync(initialValue);
```

Add the helper method to `MainWindow.axaml.cs`:

```csharp
private async Task<string?> ShowLanguageCodeDialogAsync(string initialValue)
{
    var dialog = new LanguageCodeDialog(initialValue);
    await dialog.ShowDialog(this);
    return dialog.Result;
}
```

- [ ] **Step 6: Add LocalizationFormat combobox to `SettingsWindow.axaml`**

Increase `Height` from `130` to `175`. Inside the `<StackPanel Grid.Row="0">`, after the backup directory `DockPanel`, add:

```xml
<!-- Export format -->
<DockPanel ToolTip.Tip="{StaticResource ToolTip_LocalizationFormat}">
    <TextBlock Classes="label" Text="{StaticResource Settings_LocalizationFormat}"/>
    <ComboBox SelectedItem="{Binding LocalizationFormat}"
              ToolTip.Tip="{StaticResource ToolTip_LocalizationFormat}"
              Background="#1a1a1a" Foreground="#e8e8e8"
              BorderBrush="#444" FontSize="11"
              HorizontalAlignment="Stretch">
        <ComboBoxItem Content="Csv"/>
        <ComboBoxItem Content="Json"/>
        <ComboBoxItem Content="Xliff"/>
    </ComboBox>
</DockPanel>
```

- [ ] **Step 7: Build and launch**

```
dotnet build
dotnet run --project DialogEditor.Avalonia
```

Verify:
- File menu shows "Export for Translation…" and "Import Translation…" (greyed out with no project open)
- Open a project — both menu items become active
- Export: LanguageCodeDialog opens pre-populated with current language; save dialog opens; file is written
- Import: open dialog; LanguageCodeDialog opens; project becomes dirty
- Settings window shows "Export format" combobox with Csv/Json/Xliff options

- [ ] **Step 8: Commit**

```
git add DialogEditor.Avalonia/Resources/Strings.axaml
git add DialogEditor.Avalonia/Views/MainWindow.axaml
git add DialogEditor.Avalonia/Views/MainWindow.axaml.cs
git add DialogEditor.Avalonia/Views/LanguageCodeDialog.axaml
git add DialogEditor.Avalonia/Views/LanguageCodeDialog.axaml.cs
git add DialogEditor.Avalonia/Views/SettingsWindow.axaml
git commit -m "feat: localization export/import UI — menu items, LanguageCodeDialog, settings combobox"
```

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Task |
|-----------------|------|
| `NodeTranslation` record | Task 1 |
| `ConversationPatch.Translations` (all languages equal) | Task 1 |
| `ConversationPatch.NodeComments` (any node) | Task 1 |
| `IsEmpty` updated | Task 1 |
| Schema v2 | Task 1 |
| DiffEngine produces Translations, not FieldChanges | Task 2 |
| PatchApplier structure-only | Task 3 |
| `GetStringTablePath(file, language)` | Task 4 |
| `SaveConversation` structure-only | Task 4 |
| `StringTableSerializer` NodeTranslation overload | Task 5 |
| `TranslationApplier` (installed-language filter) | Task 6 |
| `LocalizationExportFormat` enum | Task 7 |
| CSV export with writer comments | Task 7 |
| JSON export with writer comments | Task 7 |
| XLIFF 1.2 export with female/writer notes | Task 7 |
| CSV import | Task 8 |
| JSON import (ignores writer comments) | Task 8 |
| XLIFF import | Task 8 |
| CLI calls `WriteTranslations` | Task 9 |
| `ToConversation` restores text from Translations | Task 9 |
| `AppSettings.DefaultLocalizationFormat` | Task 10 |
| `SettingsViewModel.LocalizationFormat` | Task 10 |
| `ExportForTranslationCommand` | Task 11 |
| `ImportTranslationCommand` | Task 11 |
| File menu items | Task 12 |
| `LanguageCodeDialog` | Task 12 |
| String keys | Task 12 |
| Settings combobox | Task 12 |

All spec requirements are covered. No placeholders in the plan.
