# Parameter Readability — Beyond Characters: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every condition/script parameter that points at a known game-data kind an AutoCompleteBox backed by a correct name-lookup table, replacing the current behaviour where all `Guid` params share a speaker-names dropdown.

**Architecture:** Add an optional `"lookupKind"` string to each parameter in `scripts.json`/`conditions.json`. A new `GameDataNameService` registry (ViewModels layer) maps kind → `IReadOnlyList<NamedEntry>`. Game providers implement `LoadGameDataNames()` (Core layer) which parsers populate from game files; `MainWindowViewModel.LoadDirectory` registers all kinds at game-folder-open time. `ParameterValueViewModel` gains `HasLookup` / `Suggestions` replacing the old `IsGuidType` / `GuidSuggestions` pair.

**Tech Stack:** C# 12, .NET 8, CommunityToolkit.Mvvm, System.Text.Json, Avalonia XAML

## Global Constraints

- Follow strict TDD: write a failing test before any implementation code.
- No hard-coded user-visible text — all new strings go in `Strings.axaml` / `SharedStrings.axaml`.
- Every new control needs `ToolTip.Tip` + `AutomationProperties.Name`; every new focusable control needs `AutomationProperties.HelpText`.
- `DialogEditor.Core` must not reference `DialogEditor.ViewModels`.
- Tests run serially (no `[Collection(…)]` parallelism) — do not add parallel test collections.
- Log every caught exception via `AppLog.Error` or `AppLog.Warn`; swallow only `OperationCanceledException`.
- Both `DialogEditor.ViewModels/Resources/scripts.json` and `conditions.json` are the embedded-resource source of truth; `data/` copies are secondary.

---

## File Map

**New files:**
| File | Responsibility |
|---|---|
| `DialogEditor.Core/Models/GameDataEntry.cs` | Raw `(Id, Name)` pair returned by Core parsers |
| `DialogEditor.ViewModels/Services/NamedEntry.cs` | Display `(DisplayName, StoredValue)` pair for VM/UI |
| `DialogEditor.ViewModels/Services/GameDataNameService.cs` | Static registry: kind → `IReadOnlyList<NamedEntry>` |
| `DialogEditor.Core/Parsing/Poe2GameDataBundleParser.cs` | Parses `{GameDataObjects:[{ID,DebugName}]}` bundles |
| `DialogEditor.Core/Parsing/GlobalVariablesCsvParser.cs` | Parses `GlobalVariables.csv` variable name column |
| `DialogEditor.Tests/Services/GameDataNameServiceTests.cs` | Unit tests for the registry |
| `DialogEditor.Tests/Parsing/Poe2GameDataBundleParserTests.cs` | Parser tests with inline fixture JSON |
| `DialogEditor.Tests/Parsing/GlobalVariablesCsvParserTests.cs` | Parser tests with inline fixture CSV |
| `DialogEditor.Tests/Services/LookupKindWhitelistTests.cs` | Build-time guard: all LookupKind values are known |

**Modified files:**
| File | Change |
|---|---|
| `DialogEditor.ViewModels/Services/ConditionCatalogue.cs` | Add `LookupKind?` to `ConditionParameter` record |
| `DialogEditor.Core/GameData/IGameDataProvider.cs` | Add `LoadGameDataNames()` method |
| `DialogEditor.Core/GameData/Poe2GameDataProvider.cs` | Implement `LoadGameDataNames()` |
| `DialogEditor.Core/GameData/Poe1GameDataProvider.cs` | Implement `LoadGameDataNames()` |
| `DialogEditor.ViewModels/ViewModels/ConditionRowViewModel.cs` | Refactor `ParameterValueViewModel`; update `ConditionRowViewModel` instantiation |
| `DialogEditor.ViewModels/ViewModels/ScriptEditorViewModel.cs` | Update `ScriptRowViewModel` instantiation |
| `DialogEditor.Avalonia/Views/ScriptEditorWindow.axaml` | Rebind AutoCompleteBox to `HasLookup`/`Suggestions` |
| `DialogEditor.Avalonia/Views/ConditionEditorWindow.axaml` | Same |
| `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` | Register all kinds after game folder opens |
| `DialogEditor.ViewModels/Resources/scripts.json` | Add `"lookupKind"` to parameters |
| `DialogEditor.ViewModels/Resources/conditions.json` | Add `"lookupKind"` to parameters |
| `DialogEditor.Tests/ViewModels/ParameterValueViewModelTests.cs` | Replace `IsGuidType`/`GuidSuggestions` tests |
| `Gaps.md` | Mark gap implemented |

---

## Task 1: GameDataEntry + NamedEntry + GameDataNameService

**Files:**
- Create: `DialogEditor.Core/Models/GameDataEntry.cs`
- Create: `DialogEditor.ViewModels/Services/NamedEntry.cs`
- Create: `DialogEditor.ViewModels/Services/GameDataNameService.cs`
- Create: `DialogEditor.Tests/Services/GameDataNameServiceTests.cs`

**Interfaces:**
- Produces:
  - `GameDataEntry(string Id, string Name)` — used by Core parsers and `IGameDataProvider.LoadGameDataNames()`
  - `NamedEntry(string DisplayName, string StoredValue)` — used by `GameDataNameService`, `ParameterValueViewModel`
  - `GameDataNameService.Register(string kind, IReadOnlyList<NamedEntry> entries)`
  - `GameDataNameService.Get(string kind) → IReadOnlyList<NamedEntry>`
  - `GameDataNameService.Clear()`

- [ ] **Step 1: Write the failing tests**

```csharp
// DialogEditor.Tests/Services/GameDataNameServiceTests.cs
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Services;

public class GameDataNameServiceTests : IDisposable
{
    public void Dispose() => GameDataNameService.Clear();

    [Fact]
    public void Get_UnregisteredKind_ReturnsEmpty()
        => Assert.Empty(GameDataNameService.Get("Quest"));

    [Fact]
    public void Register_ThenGet_ReturnsEntries()
    {
        var entries = new[] { new NamedEntry("My Quest — abc", "abc") };
        GameDataNameService.Register("Quest", entries);
        Assert.Equal(entries, GameDataNameService.Get("Quest"));
    }

    [Fact]
    public void Register_Twice_ReplacesEntries()
    {
        GameDataNameService.Register("Quest", [new NamedEntry("Old", "old")]);
        GameDataNameService.Register("Quest", [new NamedEntry("New", "new")]);
        Assert.Equal("New", GameDataNameService.Get("Quest")[0].DisplayName);
        Assert.Single(GameDataNameService.Get("Quest"));
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        GameDataNameService.Register("Quest", [new NamedEntry("Q", "q")]);
        GameDataNameService.Clear();
        Assert.Empty(GameDataNameService.Get("Quest"));
    }

    [Fact]
    public void Get_DifferentKinds_AreIndependent()
    {
        GameDataNameService.Register("Quest", [new NamedEntry("Q", "q")]);
        GameDataNameService.Register("Item",  [new NamedEntry("I", "i")]);
        Assert.Single(GameDataNameService.Get("Quest"));
        Assert.Single(GameDataNameService.Get("Item"));
    }
}
```

- [ ] **Step 2: Run tests — expect compilation failure**

```
dotnet test "DialogEditor.Tests/DialogEditor.Tests.csproj" --filter "FullyQualifiedName~GameDataNameServiceTests"
```
Expected: compile error — `GameDataNameService` and `NamedEntry` don't exist yet.

- [ ] **Step 3: Create GameDataEntry**

```csharp
// DialogEditor.Core/Models/GameDataEntry.cs
namespace DialogEditor.Core.Models;

/// Raw name + identifier pair returned by game-data parsers.
/// Id is empty for string-keyed kinds (GlobalVariable, PoE1 item names).
public record GameDataEntry(string Id, string Name);
```

- [ ] **Step 4: Create NamedEntry**

```csharp
// DialogEditor.ViewModels/Services/NamedEntry.cs
namespace DialogEditor.ViewModels.Services;

/// Display string + stored value pair for AutoCompleteBox suggestions.
/// For GUID kinds: DisplayName = "Edér — guid", StoredValue = "guid".
/// For string kinds: DisplayName = StoredValue = variable/item name.
public record NamedEntry(string DisplayName, string StoredValue);
```

- [ ] **Step 5: Create GameDataNameService**

```csharp
// DialogEditor.ViewModels/Services/GameDataNameService.cs
namespace DialogEditor.ViewModels.Services;

public static class GameDataNameService
{
    private static readonly Dictionary<string, IReadOnlyList<NamedEntry>> _registry = new();

    public static void Register(string kind, IReadOnlyList<NamedEntry> entries)
        => _registry[kind] = entries;

    public static IReadOnlyList<NamedEntry> Get(string kind)
        => _registry.TryGetValue(kind, out var e) ? e : [];

    public static void Clear() => _registry.Clear();
}
```

- [ ] **Step 6: Run tests — expect green**

```
dotnet test "DialogEditor.Tests/DialogEditor.Tests.csproj" --filter "FullyQualifiedName~GameDataNameServiceTests"
```
Expected: 5 tests pass.

- [ ] **Step 7: Commit**

```
git add DialogEditor.Core/Models/GameDataEntry.cs DialogEditor.ViewModels/Services/NamedEntry.cs DialogEditor.ViewModels/Services/GameDataNameService.cs DialogEditor.Tests/Services/GameDataNameServiceTests.cs
git commit -m "feat(params): add GameDataEntry, NamedEntry, GameDataNameService registry"
```

---

## Task 2: Poe2GameDataBundleParser

**Files:**
- Create: `DialogEditor.Core/Parsing/Poe2GameDataBundleParser.cs`
- Create: `DialogEditor.Tests/Parsing/Poe2GameDataBundleParserTests.cs`

**Interfaces:**
- Consumes: `GameDataEntry` from Task 1
- Produces:
  - `Poe2GameDataBundleParser.Parse(string json, Func<string,string>? cleanName = null) → IReadOnlyList<GameDataEntry>`
  - `Poe2GameDataBundleParser.ParseFile(string path, Func<string,string>? cleanName = null) → IReadOnlyList<GameDataEntry>`

- [ ] **Step 1: Write the failing tests**

```csharp
// DialogEditor.Tests/Parsing/Poe2GameDataBundleParserTests.cs
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;
using Xunit;

namespace DialogEditor.Tests.Parsing;

public class Poe2GameDataBundleParserTests
{
    private const string SpeakerFixture = """
        {
          "GameDataObjects": [
            { "ID": "aaaaaaaa-0000-0000-0000-000000000001", "DebugName": "SPK_Companion_Eder" },
            { "ID": "bbbbbbbb-0000-0000-0000-000000000002", "DebugName": "SPK_NPC_Innkeeper" }
          ]
        }
        """;

    private const string QuestFixture = """
        {
          "GameDataObjects": [
            { "ID": "cccccccc-0000-0000-0000-000000000003", "DebugName": "Q01_MainQuest" },
            { "ID": "dddddddd-0000-0000-0000-000000000004", "DebugName": "Q02_SideQuest" }
          ]
        }
        """;

    [Fact]
    public void Parse_ExtractsIdAndDebugName()
    {
        var entries = Poe2GameDataBundleParser.Parse(QuestFixture);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Id == "cccccccc-0000-0000-0000-000000000003");
    }

    [Fact]
    public void Parse_AppliesCleanName()
    {
        var entries = Poe2GameDataBundleParser.Parse(
            SpeakerFixture,
            name => name.Replace("SPK_Companion_", "").Replace("SPK_NPC_", ""));
        Assert.Contains(entries, e => e.Name == "Eder");
        Assert.Contains(entries, e => e.Name == "Innkeeper");
    }

    [Fact]
    public void Parse_WithoutCleanName_UsesDebugNameAsIs()
    {
        var entries = Poe2GameDataBundleParser.Parse(QuestFixture);
        Assert.Contains(entries, e => e.Name == "Q01_MainQuest");
    }

    [Fact]
    public void Parse_EmptyObjects_ReturnsEmpty()
    {
        var entries = Poe2GameDataBundleParser.Parse("""{"GameDataObjects":[]}""");
        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_SkipsEntriesWithBlankIdOrName()
    {
        const string json = """
            {
              "GameDataObjects": [
                { "ID": "", "DebugName": "HasNoId" },
                { "ID": "eeeeeeee-0000-0000-0000-000000000005", "DebugName": "" },
                { "ID": "ffffffff-0000-0000-0000-000000000006", "DebugName": "Valid" }
              ]
            }
            """;
        var entries = Poe2GameDataBundleParser.Parse(json);
        Assert.Single(entries);
        Assert.Equal("Valid", entries[0].Name);
    }

    [Fact]
    public void Parse_WorksForDifferentKindFixtures()
    {
        // Confirms the parser is generic — same code handles Quests and Speakers
        var speakers = Poe2GameDataBundleParser.Parse(SpeakerFixture);
        var quests   = Poe2GameDataBundleParser.Parse(QuestFixture);
        Assert.Equal(2, speakers.Count);
        Assert.Equal(2, quests.Count);
    }

    [Fact]
    public void ParseFile_NonExistentPath_ReturnsEmpty()
        => Assert.Empty(Poe2GameDataBundleParser.ParseFile(@"C:\does\not\exist.gamedatabundle"));
}
```

- [ ] **Step 2: Run — expect compile failure**

```
dotnet test "DialogEditor.Tests/DialogEditor.Tests.csproj" --filter "FullyQualifiedName~Poe2GameDataBundleParserTests"
```

- [ ] **Step 3: Create Poe2GameDataBundleParser**

```csharp
// DialogEditor.Core/Parsing/Poe2GameDataBundleParser.cs
using System.Text.Json;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

public static class Poe2GameDataBundleParser
{
    private record BundleRoot(List<BundleObject> GameDataObjects);
    private record BundleObject(string Id, string DebugName);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<GameDataEntry> Parse(
        string json,
        Func<string, string>? cleanName = null)
    {
        var root = JsonSerializer.Deserialize<BundleRoot>(json, Options);
        if (root is null) return [];

        return root.GameDataObjects
            .Where(o => !string.IsNullOrWhiteSpace(o.Id)
                     && !string.IsNullOrWhiteSpace(o.DebugName))
            .Select(o => new GameDataEntry(
                Id:   o.Id,
                Name: cleanName?.Invoke(o.DebugName) ?? o.DebugName))
            .ToList();
    }

    public static IReadOnlyList<GameDataEntry> ParseFile(
        string path,
        Func<string, string>? cleanName = null)
    {
        if (!File.Exists(path)) return [];
        var text = File.ReadAllText(path,
            new System.Text.UTF8Encoding(detectEncodingFromByteOrderMarks: true));
        return Parse(text, cleanName);
    }
}
```

- [ ] **Step 4: Run — expect green**

```
dotnet test "DialogEditor.Tests/DialogEditor.Tests.csproj" --filter "FullyQualifiedName~Poe2GameDataBundleParserTests"
```

- [ ] **Step 5: Commit**

```
git add DialogEditor.Core/Parsing/Poe2GameDataBundleParser.cs DialogEditor.Tests/Parsing/Poe2GameDataBundleParserTests.cs
git commit -m "feat(params): add Poe2GameDataBundleParser"
```

---

## Task 3: GlobalVariablesCsvParser

**Files:**
- Create: `DialogEditor.Core/Parsing/GlobalVariablesCsvParser.cs`
- Create: `DialogEditor.Tests/Parsing/GlobalVariablesCsvParserTests.cs`

**Interfaces:**
- Consumes: `GameDataEntry` from Task 1
- Produces:
  - `GlobalVariablesCsvParser.Parse(string csvText) → IReadOnlyList<GameDataEntry>`
  - `GlobalVariablesCsvParser.ParseFile(string path) → IReadOnlyList<GameDataEntry>`
  - For string-keyed kinds: `Id = ""`, `Name = variableName` so `GameDataEntry.Id` being empty signals "no GUID lookup needed".

- [ ] **Step 1: Write failing tests**

```csharp
// DialogEditor.Tests/Parsing/GlobalVariablesCsvParserTests.cs
using DialogEditor.Core.Parsing;
using Xunit;

namespace DialogEditor.Tests.Parsing;

public class GlobalVariablesCsvParserTests
{
    // Typical GlobalVariables.csv: first column is the variable name; header row present.
    private const string Fixture = """
        Name,DefaultValue,Type
        npc_met_eder,0,Int
        quest_accepted,0,Int
        player_gold,100,Int
        """;

    [Fact]
    public void Parse_ExtractsFirstColumn()
    {
        var entries = GlobalVariablesCsvParser.Parse(Fixture);
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.Name == "npc_met_eder");
        Assert.Contains(entries, e => e.Name == "quest_accepted");
        Assert.Contains(entries, e => e.Name == "player_gold");
    }

    [Fact]
    public void Parse_SkipsHeaderRow()
    {
        var entries = GlobalVariablesCsvParser.Parse(Fixture);
        Assert.DoesNotContain(entries, e => e.Name == "Name");
    }

    [Fact]
    public void Parse_IdIsEmpty_ForEveryEntry()
    {
        var entries = GlobalVariablesCsvParser.Parse(Fixture);
        Assert.All(entries, e => Assert.Empty(e.Id));
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmpty()
        => Assert.Empty(GlobalVariablesCsvParser.Parse(string.Empty));

    [Fact]
    public void Parse_HeaderOnly_ReturnsEmpty()
        => Assert.Empty(GlobalVariablesCsvParser.Parse("Name,DefaultValue,Type"));

    [Fact]
    public void ParseFile_NonExistentPath_ReturnsEmpty()
        => Assert.Empty(GlobalVariablesCsvParser.ParseFile(@"C:\does\not\exist.csv"));
}
```

> **Note:** If the real `GlobalVariables.csv` has a different format (no header, different column order), adjust the fixture and parser accordingly during the discovery step in Task 8.

- [ ] **Step 2: Run — expect compile failure**

```
dotnet test "DialogEditor.Tests/DialogEditor.Tests.csproj" --filter "FullyQualifiedName~GlobalVariablesCsvParserTests"
```

- [ ] **Step 3: Create GlobalVariablesCsvParser**

```csharp
// DialogEditor.Core/Parsing/GlobalVariablesCsvParser.cs
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

public static class GlobalVariablesCsvParser
{
    public static IReadOnlyList<GameDataEntry> Parse(string csvText)
    {
        if (string.IsNullOrWhiteSpace(csvText)) return [];

        return csvText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)                         // skip header row
            .Select(line => line.Split(',')[0].Trim().Trim('"', '\r'))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new GameDataEntry(Id: string.Empty, Name: name))
            .ToList();
    }

    public static IReadOnlyList<GameDataEntry> ParseFile(string path)
    {
        if (!File.Exists(path)) return [];
        return Parse(File.ReadAllText(path,
            new System.Text.UTF8Encoding(detectEncodingFromByteOrderMarks: true)));
    }
}
```

- [ ] **Step 4: Run — expect green**

```
dotnet test "DialogEditor.Tests/DialogEditor.Tests.csproj" --filter "FullyQualifiedName~GlobalVariablesCsvParserTests"
```

- [ ] **Step 5: Commit**

```
git add DialogEditor.Core/Parsing/GlobalVariablesCsvParser.cs DialogEditor.Tests/Parsing/GlobalVariablesCsvParserTests.cs
git commit -m "feat(params): add GlobalVariablesCsvParser"
```

---

## Task 4: ConditionParameter model + IGameDataProvider interface (with stubs)

**Files:**
- Modify: `DialogEditor.ViewModels/Services/ConditionCatalogue.cs` — add `LookupKind?` to `ConditionParameter`
- Modify: `DialogEditor.Core/GameData/IGameDataProvider.cs` — add `LoadGameDataNames()`
- Modify: `DialogEditor.Core/GameData/Poe2GameDataProvider.cs` — add stub
- Modify: `DialogEditor.Core/GameData/Poe1GameDataProvider.cs` — add stub

**Interfaces:**
- Consumes: `GameDataEntry` from Task 1
- Produces:
  - `ConditionParameter.LookupKind` — nullable string property; populated from JSON `"lookupKind"` field via case-insensitive deserialisation
  - `IGameDataProvider.LoadGameDataNames() → IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>>`

No new tests in this task — the changes are purely additive (null-safe) and will be covered by existing and future tests.

- [ ] **Step 1: Add `LookupKind?` to `ConditionParameter`**

In `DialogEditor.ViewModels/Services/ConditionCatalogue.cs`, change the `ConditionParameter` record from:

```csharp
public record ConditionParameter(
    string Name,
    string Type,
    string Description,
    string Default,
    IReadOnlyList<string>? Options = null,
    IReadOnlyList<string>? Values  = null);
```

to:

```csharp
public record ConditionParameter(
    string Name,
    string Type,
    string Description,
    string Default,
    IReadOnlyList<string>? Options    = null,
    IReadOnlyList<string>? Values     = null,
    string?                LookupKind = null);
```

`ScriptCatalogueEntry.Parameters` is already typed `IReadOnlyList<ConditionParameter>`, so scripts get the new field for free.

- [ ] **Step 2: Add `LoadGameDataNames()` to `IGameDataProvider`**

In `DialogEditor.Core/GameData/IGameDataProvider.cs`, add after `LoadSpeakerNames()`:

```csharp
/// Returns named entries grouped by lookup kind (e.g. "Quest", "Item", "GlobalVariable").
/// Called once when a game folder is opened. Returns empty dict when no data is available.
IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>> LoadGameDataNames();
```

Add the using at the top of the file:
```csharp
using DialogEditor.Core.Models;
```

- [ ] **Step 3: Add stub implementations to both providers**

In `DialogEditor.Core/GameData/Poe2GameDataProvider.cs`, add:

```csharp
public IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>> LoadGameDataNames()
    => new Dictionary<string, IReadOnlyList<GameDataEntry>>();
```

In `DialogEditor.Core/GameData/Poe1GameDataProvider.cs`, add the same stub.

Add `using DialogEditor.Core.Models;` to both provider files.

- [ ] **Step 4: Build to confirm no regressions**

```
dotnet build DialogEditor.sln
```
Expected: 0 errors, 0 warnings on the changed files.

- [ ] **Step 5: Run all tests**

```
dotnet test "DialogEditor.Tests/DialogEditor.Tests.csproj"
```
Expected: all previously passing tests still pass.

- [ ] **Step 6: Commit**

```
git add DialogEditor.ViewModels/Services/ConditionCatalogue.cs DialogEditor.Core/GameData/IGameDataProvider.cs DialogEditor.Core/GameData/Poe2GameDataProvider.cs DialogEditor.Core/GameData/Poe1GameDataProvider.cs
git commit -m "feat(params): add LookupKind to ConditionParameter; add LoadGameDataNames to IGameDataProvider"
```

---

## Task 5: ParameterValueViewModel refactor

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/ConditionRowViewModel.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/ScriptEditorViewModel.cs`
- Modify: `DialogEditor.Tests/ViewModels/ParameterValueViewModelTests.cs`

**Interfaces:**
- Consumes: `GameDataNameService.Get()` and `NamedEntry` from Task 1
- Produces (replaces `IsGuidType`/`GuidSuggestions`):
  - `ParameterValueViewModel.LookupKind { get; init; }`
  - `ParameterValueViewModel.HasLookup`
  - `ParameterValueViewModel.Suggestions`
  - `IsText` updated to `!IsEnum && !HasLookup`

- [ ] **Step 1: Replace ParameterValueViewModelTests.cs**

The file currently tests `IsGuidType`, `GuidSuggestions`, and the old string-splitting normalisation. Replace its entire contents:

```csharp
// DialogEditor.Tests/ViewModels/ParameterValueViewModelTests.cs
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class ParameterValueViewModelTests : IDisposable
{
    public void Dispose()
    {
        SpeakerNameService.Register(new Dictionary<string, string>());
        GameDataNameService.Clear();
    }

    private static ParameterValueViewModel Make(
        string type, string lookupKind = "", string value = "") =>
        new() { Name = "p", Description = "", Type = type,
                LookupKind = lookupKind, Value = value };

    // ── HasLookup ──────────────────────────────────────────────────────────

    [Fact]
    public void HasLookup_EmptyLookupKind_ReturnsFalse()
        => Assert.False(Make("Guid").HasLookup);

    [Fact]
    public void HasLookup_NonEmptyLookupKind_ReturnsTrue()
        => Assert.True(Make("Guid", "Quest").HasLookup);

    // ── IsText ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsText_True_WhenNoLookupAndNotEnum()
        => Assert.True(Make("String").IsText);

    [Fact]
    public void IsText_False_WhenHasLookup()
        => Assert.False(Make("GlobalVariable", "GlobalVariable").IsText);

    [Fact]
    public void IsText_False_WhenEnum()
        => Assert.False(Make("Boolean").IsText);

    // ── Suggestions ────────────────────────────────────────────────────────

    [Fact]
    public void Suggestions_EmptyWhenNoLookup()
        => Assert.Empty(Make("Guid").Suggestions);

    [Fact]
    public void Suggestions_ReturnsDisplayNames_FromRegisteredKind()
    {
        GameDataNameService.Register("Quest",
            [new NamedEntry("A Quest — cccccccc-0000-0000-0000-000000000003",
                            "cccccccc-0000-0000-0000-000000000003")]);
        Assert.Equal(
            ["A Quest — cccccccc-0000-0000-0000-000000000003"],
            Make("Guid", "Quest").Suggestions);
    }

    [Fact]
    public void Suggestions_EmptyWhenKindNotRegistered()
        => Assert.Empty(Make("Guid", "Quest").Suggestions);

    // ── OnValueChanged normalisation ───────────────────────────────────────

    [Fact]
    public void Value_WritesStoredValue_WhenDisplayNameSelected()
    {
        const string guid = "cccccccc-0000-0000-0000-000000000003";
        GameDataNameService.Register("Quest",
            [new NamedEntry($"A Quest — {guid}", guid)]);
        var vm = Make("Guid", "Quest");
        vm.Value = $"A Quest — {guid}";
        Assert.Equal(guid, vm.Value);
    }

    [Fact]
    public void Value_PreservesRaw_WhenNoMatchingEntry()
    {
        GameDataNameService.Register("Quest",
            [new NamedEntry("A Quest — abc", "abc")]);
        var vm = Make("Guid", "Quest");
        vm.Value = "00000000-0000-0000-0000-999999999999";
        Assert.Equal("00000000-0000-0000-0000-999999999999", vm.Value);
    }

    [Fact]
    public void Value_Unchanged_WhenNoLookup()
    {
        var vm = Make("String");
        vm.Value = "Something — not-a-guid";
        Assert.Equal("Something — not-a-guid", vm.Value);
    }

    [Fact]
    public void Value_WritesStoredValue_ForStringKind()
    {
        // For GlobalVariable: DisplayName == StoredValue, so lookup is identity.
        GameDataNameService.Register("GlobalVariable",
            [new NamedEntry("npc_met_eder", "npc_met_eder")]);
        var vm = Make("GlobalVariable", "GlobalVariable");
        vm.Value = "npc_met_eder";
        Assert.Equal("npc_met_eder", vm.Value);
    }

    // ── Speaker lookup ─────────────────────────────────────────────────────

    [Fact]
    public void Suggestions_ContainsBuiltins_WhenSpeakerKindRegistered()
    {
        var speakers = SpeakerNameService.All
            .Select(s => new NamedEntry($"{s.Name} — {s.Guid}", s.Guid))
            .ToList();
        GameDataNameService.Register("Speaker", speakers);
        var vm = Make("ObjectGuid", "Speaker");
        Assert.Contains(vm.Suggestions, s => s.Contains("Player"));
        Assert.Contains(vm.Suggestions, s => s.Contains("Narrator"));
    }
}
```

- [ ] **Step 2: Run tests — expect failures on the new tests (old tests gone)**

```
dotnet test "DialogEditor.Tests/DialogEditor.Tests.csproj" --filter "FullyQualifiedName~ParameterValueViewModelTests"
```
Expected: compile errors because `ParameterValueViewModel.LookupKind`, `HasLookup`, and `Suggestions` don't exist yet.

- [ ] **Step 3: Refactor ParameterValueViewModel in ConditionRowViewModel.cs**

In `DialogEditor.ViewModels/ViewModels/ConditionRowViewModel.cs`, inside the `ParameterValueViewModel` class (lines 9–101), make these changes:

**Add** after the `Values` property (line 15):
```csharp
public string LookupKind { get; init; } = string.Empty;
```

**Add** after `IsLabeledEnum`:
```csharp
public bool HasLookup => LookupKind.Length > 0;
```

**Remove** the `IsGuidType` property:
```csharp
// DELETE THIS:
public bool IsGuidType => Type is "ObjectGuid" or "Guid";
```

**Change** `IsText` from:
```csharp
public bool IsText => !IsEnum && !IsGuidType;
```
to:
```csharp
public bool IsText => !IsEnum && !HasLookup;
```

**Remove** `GuidSuggestions`:
```csharp
// DELETE THIS:
public IReadOnlyList<string> GuidSuggestions =>
    SpeakerNameService.All.Select(s => $"{s.Name} — {s.Guid}").ToList();
```

**Add** `Suggestions` in its place:
```csharp
public IReadOnlyList<string> Suggestions =>
    HasLookup
        ? GameDataNameService.Get(LookupKind).Select(e => e.DisplayName).ToList()
        : [];
```

**Replace** `OnValueChanged` (lines 92–100):
```csharp
partial void OnValueChanged(string value)
{
    if (!HasLookup) return;
    var entry = GameDataNameService.Get(LookupKind)
        .FirstOrDefault(e => e.DisplayName == value);
    if (entry is not null)
        Value = entry.StoredValue;
}
```

**Update** the TypeHint switch for "ObjectGuid" and "Guid" (the old text referred to "companion list"):
```csharp
"ObjectGuid" => "GUID of an in-scene game object. Type a name or GUID prefix to search, or paste any GUID directly.",
"Guid"       => "GUID value. Type a name or GUID prefix to search, or paste any GUID directly.",
```

Add `using DialogEditor.ViewModels.Services;` at the top if not already present.

- [ ] **Step 4: Update ConditionRowViewModel instantiation (lines ~172–181)**

In the `ConditionRowViewModel(ConditionLeaf leaf, ...)` constructor, add `LookupKind`:
```csharp
return new ParameterValueViewModel
{
    Name        = p.Name,
    Description = p.Description,
    Type        = p.Type,
    Options     = p.Options,
    Values      = p.Values,
    LookupKind  = p.LookupKind ?? string.Empty,
    Value       = display,
};
```

- [ ] **Step 5: Update ScriptRowViewModel instantiation**

In `DialogEditor.ViewModels/ViewModels/ScriptEditorViewModel.cs`, in `ScriptRowViewModel(ScriptCall call, ScriptCatalogueEntry? entry)` (lines 29–37), add `LookupKind`:
```csharp
new ParameterValueViewModel
{
    Name        = p.Name,
    Description = p.Description,
    Type        = p.Type,
    Options     = p.Options,
    LookupKind  = p.LookupKind ?? string.Empty,
    Value       = i < call.Parameters.Count ? call.Parameters[i] : p.Default,
}
```

- [ ] **Step 6: Run all tests — expect green**

```
dotnet test "DialogEditor.Tests/DialogEditor.Tests.csproj"
```
Expected: all tests pass. (The old `IsGuidType` / `GuidSuggestions` tests are gone; new `HasLookup` / `Suggestions` tests pass.)

- [ ] **Step 7: Commit**

```
git add DialogEditor.ViewModels/ViewModels/ConditionRowViewModel.cs DialogEditor.ViewModels/ViewModels/ScriptEditorViewModel.cs DialogEditor.Tests/ViewModels/ParameterValueViewModelTests.cs
git commit -m "feat(params): refactor ParameterValueViewModel — HasLookup/Suggestions replaces IsGuidType/GuidSuggestions"
```

---

## Task 6: XAML rebinding + MainWindowViewModel wiring

**Files:**
- Modify: `DialogEditor.Avalonia/Views/ScriptEditorWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/ConditionEditorWindow.axaml`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`

No new tests — the XAML changes are binding renames; wiring is covered by integration (game folder opens and suggestions appear).

- [ ] **Step 1: Update ScriptEditorWindow.axaml**

Find the AutoCompleteBox that is currently bound to `IsGuidType` and `GuidSuggestions` (around lines 125–133). Change:
- `IsVisible="{Binding IsGuidType}"` → `IsVisible="{Binding HasLookup}"`
- `ItemsSource="{Binding GuidSuggestions}"` → `ItemsSource="{Binding Suggestions}"`

The `TextBox` whose `IsVisible="{Binding IsText}"` and the enum `AutoCompleteBox` need no changes.

- [ ] **Step 2: Update ConditionEditorWindow.axaml**

Find the AutoCompleteBox bound to `IsGuidType` and `GuidSuggestions` (around lines 199–208). Apply the same renames:
- `IsVisible="{Binding IsGuidType}"` → `IsVisible="{Binding HasLookup}"`
- `ItemsSource="{Binding GuidSuggestions}"` → `ItemsSource="{Binding Suggestions}"`

- [ ] **Step 3: Add GameDataNameService registration to MainWindowViewModel.LoadDirectory**

In `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`, find the `LoadDirectory` method (around line 994). Immediately after the existing speaker registration:

```csharp
SpeakerNameService.Register(provider.LoadSpeakerNames());
```

Add:

```csharp
// Populate GameDataNameService for all lookup kinds.
// Speaker kind is populated from the already-loaded speaker data to avoid re-parsing.
GameDataNameService.Clear();
var speakerEntries = SpeakerNameService.All
    .Select(s => new NamedEntry($"{s.Name} — {s.Guid}", s.Guid))
    .ToList();
GameDataNameService.Register("Speaker", speakerEntries);

foreach (var (kind, entries) in provider.LoadGameDataNames())
{
    var namedEntries = entries
        .Select(e => string.IsNullOrEmpty(e.Id)
            ? new NamedEntry(e.Name, e.Name)
            : new NamedEntry($"{e.Name} — {e.Id}", e.Id))
        .OrderBy(ne => ne.DisplayName)
        .ToList();
    GameDataNameService.Register(kind, namedEntries);
}
```

Add `using DialogEditor.ViewModels.Services;` and `using DialogEditor.Core.Models;` at the top of `MainWindowViewModel.cs` if not already present.

- [ ] **Step 4: Build**

```
dotnet build DialogEditor.sln
```
Expected: 0 errors.

- [ ] **Step 5: Run all tests**

```
dotnet test "DialogEditor.Tests/DialogEditor.Tests.csproj"
```
Expected: all pass.

- [ ] **Step 6: Commit**

```
git add DialogEditor.Avalonia/Views/ScriptEditorWindow.axaml DialogEditor.Avalonia/Views/ConditionEditorWindow.axaml DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git commit -m "feat(params): wire HasLookup/Suggestions in XAML; register GameDataNameService on game folder open"
```

---

## Task 7: PoE2 game data discovery + Poe2GameDataProvider.LoadGameDataNames()

> **This task starts with a required manual research step.** The exact `.gamedatabundle` filenames and whether each uses the standard `{GameDataObjects:[{ID, DebugName}]}` envelope must be confirmed against a real PoE2 game installation before writing any parsing code.

**Files:**
- Modify: `DialogEditor.Core/GameData/Poe2GameDataProvider.cs`

- [ ] **Step 1: Discover PoE2 game data bundle filenames**

With a PoE2 installation available:

1. Navigate to `{poe2Root}/PillarsOfEternityII_Data/exported/design/gamedata/`
2. List all `.gamedatabundle` files: `dir *.gamedatabundle` (Windows) or `ls *.gamedatabundle` (bash)
3. For each target kind from the table below, find the matching bundle filename
4. Open one candidate file and confirm it has the `GameDataObjects` array with `ID` and `DebugName` fields (or note the actual field names if different)
5. Confirm the path and filename for `GlobalVariables.csv` (likely in the same `gamedata/` directory or in `design/`)

Target kinds and expected filenames (confirm or correct each):

| Kind | Expected filename | Confirmed? |
|---|---|---|
| Quest | `quests.gamedatabundle` | |
| Item | `items.gamedatabundle` | |
| Ability | `abilities.gamedatabundle` | |
| Class | `classes.gamedatabundle` | |
| Race | `races.gamedatabundle` | |
| Subrace | `subraces.gamedatabundle` | |
| Background | `backgrounds.gamedatabundle` | |
| Culture | `cultures.gamedatabundle` | |
| Deity | `deities.gamedatabundle` | |
| PaladinOrder | `paladinorders.gamedatabundle` | |
| Faction | `factions.gamedatabundle` | |
| Disposition | `dispositions.gamedatabundle` | |
| DispositionStrength | `dispositionstrengths.gamedatabundle` | |
| Skill | `skills.gamedatabundle` | |
| Phrase | `phrases.gamedatabundle` | |
| Keyword | `keywords.gamedatabundle` | |
| StatusEffect | `statuseffects.gamedatabundle` | |
| CreatureType | `creaturetypes.gamedatabundle` | |
| WeaponType | `weapontypes.gamedatabundle` | |
| ArmorType | `armortypes.gamedatabundle` | |
| Map | verify structure — may differ | |
| Conversation | may use conversationbundle listing instead | |

> If a bundle uses a different field (e.g. `"Name"` instead of `"DebugName"`), pass a custom `cleanName` delegate or add a field-name parameter to `Poe2GameDataBundleParser.Parse`. If `Map` or `Conversation` need a bespoke parser, create one in a follow-up commit within this task.

- [ ] **Step 2: Implement Poe2GameDataProvider.LoadGameDataNames()**

Replace the stub in `DialogEditor.Core/GameData/Poe2GameDataProvider.cs` with the real implementation. Add a `GameDataRoot` computed property alongside the existing path properties:

```csharp
private string GameDataRoot => Path.Combine(ExportedRoot, "design", "gamedata");
```

Then implement `LoadGameDataNames()` using the confirmed filenames from Step 1:

```csharp
public IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>> LoadGameDataNames()
{
    var result = new Dictionary<string, IReadOnlyList<GameDataEntry>>();

    void Bundle(string kind, string filename, Func<string, string>? clean = null)
    {
        var path = Path.Combine(GameDataRoot, filename);
        var entries = Poe2GameDataBundleParser.ParseFile(path, clean);
        if (entries.Count > 0) result[kind] = entries;
    }

    // Fill in confirmed filenames from discovery step:
    Bundle("Quest",               "quests.gamedatabundle");
    Bundle("Item",                "items.gamedatabundle");
    Bundle("Ability",             "abilities.gamedatabundle");
    Bundle("Class",               "classes.gamedatabundle");
    Bundle("Race",                "races.gamedatabundle");
    Bundle("Subrace",             "subraces.gamedatabundle");
    Bundle("Background",          "backgrounds.gamedatabundle");
    Bundle("Culture",             "cultures.gamedatabundle");
    Bundle("Deity",               "deities.gamedatabundle");
    Bundle("PaladinOrder",        "paladinorders.gamedatabundle");
    Bundle("Faction",             "factions.gamedatabundle");
    Bundle("Disposition",         "dispositions.gamedatabundle");
    Bundle("DispositionStrength", "dispositionstrengths.gamedatabundle");
    Bundle("Skill",               "skills.gamedatabundle");
    Bundle("Phrase",              "phrases.gamedatabundle");
    Bundle("Keyword",             "keywords.gamedatabundle");
    Bundle("StatusEffect",        "statuseffects.gamedatabundle");
    Bundle("CreatureType",        "creaturetypes.gamedatabundle");
    Bundle("WeaponType",          "weapontypes.gamedatabundle");
    Bundle("ArmorType",           "armortypes.gamedatabundle");
    // Map and Conversation: fill in after discovery; omit if no suitable source exists.

    // GlobalVariables.csv
    var csvPath = Path.Combine(GameDataRoot, "GlobalVariables.csv");
    var vars = GlobalVariablesCsvParser.ParseFile(csvPath);
    if (vars.Count > 0) result["GlobalVariable"] = vars;

    return result;
}
```

> Add `using DialogEditor.Core.Parsing;` at the top of the file.

- [ ] **Step 3: Build and manual smoke test**

```
dotnet build DialogEditor.sln
```

Open the editor, load a PoE2 game folder, open the Script Editor on any node, add a `StartQuest` script, and confirm the `Quest GUID` field shows a populated AutoCompleteBox with quest names.

- [ ] **Step 4: Commit**

```
git add DialogEditor.Core/GameData/Poe2GameDataProvider.cs
git commit -m "feat(params): implement Poe2GameDataProvider.LoadGameDataNames() with all PoE2 bundle kinds"
```

---

## Task 8: PoE1 game data discovery + Poe1GameDataProvider.LoadGameDataNames()

> **Another manual research step.** PoE1 uses XML-based game data, not gamedatabundles. The file structure and schema for items, abilities, quests, etc. must be confirmed against a real PoE1 installation.

**Files:**
- Modify: `DialogEditor.Core/GameData/Poe1GameDataProvider.cs`
- Optionally create: `DialogEditor.Core/Parsing/Poe1ItemNameParser.cs`, `Poe1AbilityNameParser.cs`, etc. (one per data kind that needs a bespoke XML parser)

- [ ] **Step 1: Discover PoE1 game data file structure**

With a PoE1 installation available:

1. Navigate to `{poe1Root}/PillarsOfEternity_Data/data/`
2. List subdirectories: which ones hold item/ability/quest/global-variable data?
3. Open one item file and note the XML schema (element names, attribute names for the identifier and display name)
4. Confirm whether `GlobalVariables.csv` exists (try `data/globalvariables/GlobalVariables.csv` or similar)

Target kinds for PoE1 (String-typed params):

| Kind | Expected source | Notes |
|---|---|---|
| Item | XML in `data/items/`? | PoE1 scripts reference items by internal name |
| Ability | XML in `data/abilities/`? | Talent/ability internal names |
| Quest | XML in `data/quests/`? | Quest internal names |
| GlobalVariable | `GlobalVariables.csv`? | Same CSV format as PoE2? |

PoE1 GUID-typed params (ObjectGuid = speaker names) are already handled by the existing `LoadSpeakerNames()` + Speaker kind registration in `MainWindowViewModel`. No new parser needed for Speaker.

> If a data kind's source files don't exist, exist but have unusable structure, or would require significant additional work to parse, leave that kind unregistered and note it in Gaps.md as a deferred follow-up.

- [ ] **Step 2: Create PoE1 parsers for confirmed kinds**

For each confirmed data kind, create a parser in `DialogEditor.Core/Parsing/` following the same pattern as `Poe1SpeakerNameParser`: a static `ParseFromDisk(string rootPath)` method returning `IReadOnlyList<GameDataEntry>` where `Id = ""` and `Name = internalName`.

Example structure (adapt to actual XML schema found in Step 1):

```csharp
// DialogEditor.Core/Parsing/Poe1ItemNameParser.cs
using System.Xml.Linq;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

public static class Poe1ItemNameParser
{
    public static IReadOnlyList<GameDataEntry> ParseFromDisk(string dataRoot)
    {
        var itemsDir = Path.Combine(dataRoot, "items");  // adjust to actual path
        if (!Directory.Exists(itemsDir)) return [];

        return Directory
            .EnumerateFiles(itemsDir, "*.item", SearchOption.AllDirectories)
            .Select(TryParseFile)
            .Where(e => e is not null)
            .Cast<GameDataEntry>()
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static GameDataEntry? TryParseFile(string path)
    {
        try
        {
            var doc  = XDocument.Load(path);
            var name = doc.Root?.Attribute("DebugName")?.Value  // adjust attribute name
                    ?? Path.GetFileNameWithoutExtension(path);
            return new GameDataEntry(Id: string.Empty, Name: name);
        }
        catch { return null; }
    }
}
```

- [ ] **Step 3: Implement Poe1GameDataProvider.LoadGameDataNames()**

Replace the stub with the real implementation calling the parsers created in Step 2:

```csharp
public IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>> LoadGameDataNames()
{
    var result = new Dictionary<string, IReadOnlyList<GameDataEntry>>();

    // Add each confirmed PoE1 kind here:
    var items = Poe1ItemNameParser.ParseFromDisk(DataRoot);
    if (items.Count > 0) result["Item"] = items;

    // GlobalVariables.csv (try game data root; adjust path if needed)
    var csvPath = Path.Combine(DataRoot, "globalvariables", "GlobalVariables.csv");
    var vars = GlobalVariablesCsvParser.ParseFile(csvPath);
    if (vars.Count > 0) result["GlobalVariable"] = vars;

    return result;
}
```

- [ ] **Step 4: Manual smoke test**

Open the editor with a PoE1 game folder. Open the Script Editor on any node, add a `GiveItem` script (PoE1 variant), and confirm the `Item Name` field shows a populated AutoCompleteBox with item names.

- [ ] **Step 5: Commit**

```
git add DialogEditor.Core/GameData/Poe1GameDataProvider.cs DialogEditor.Core/Parsing/Poe1*NameParser.cs
git commit -m "feat(params): implement Poe1GameDataProvider.LoadGameDataNames() with PoE1 item/ability/global-variable parsers"
```

---

## Task 9: Catalogue JSON annotation + LookupKind whitelist test

**Files:**
- Modify: `DialogEditor.ViewModels/Resources/scripts.json`
- Modify: `DialogEditor.ViewModels/Resources/conditions.json`
- Create: `DialogEditor.Tests/Services/LookupKindWhitelistTests.cs`

- [ ] **Step 1: Write the whitelist test first**

```csharp
// DialogEditor.Tests/Services/LookupKindWhitelistTests.cs
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Services;

public class LookupKindWhitelistTests
{
    private static readonly HashSet<string> KnownKinds = new(StringComparer.Ordinal)
    {
        "Speaker", "Quest", "Item", "Ability", "GlobalVariable",
        "Class", "Race", "Subrace", "Background", "Culture",
        "Deity", "PaladinOrder", "Faction", "Disposition",
        "DispositionStrength", "Skill", "Phrase", "Keyword",
        "StatusEffect", "CreatureType", "Map", "Conversation",
        "WeaponType", "ArmorType"
    };

    [Fact]
    public void ConditionCatalogue_AllLookupKinds_AreInWhitelist()
    {
        var kinds = ConditionCatalogue.Instance.All
            .SelectMany(e => e.Parameters)
            .Select(p => p.LookupKind)
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct();

        foreach (var kind in kinds)
            Assert.Contains(kind, KnownKinds);
    }

    [Fact]
    public void ScriptCatalogue_AllLookupKinds_AreInWhitelist()
    {
        var kinds = ScriptCatalogue.Instance.All
            .SelectMany(e => e.Parameters)
            .Select(p => p.LookupKind)
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct();

        foreach (var kind in kinds)
            Assert.Contains(kind, KnownKinds);
    }
}
```

- [ ] **Step 2: Run — expect green (no LookupKind annotations exist yet, so zero kinds to check)**

```
dotnet test "DialogEditor.Tests/DialogEditor.Tests.csproj" --filter "FullyQualifiedName~LookupKindWhitelistTests"
```
Expected: 2 tests pass (vacuously — no annotations yet).

- [ ] **Step 3: Annotate scripts.json**

Open `DialogEditor.ViewModels/Resources/scripts.json`. For each parameter entry, add a `"lookupKind"` field according to these rules:

**By Type:**
- `"type": "ObjectGuid"` → always add `"lookupKind": "Speaker"`
- `"type": "GlobalVariable"` → always add `"lookupKind": "GlobalVariable"`

**`"type": "Guid"` or `"type": "GameData"` — by parameter name:**

| If name contains… | Add `"lookupKind"` |
|---|---|
| Quest | `"Quest"` |
| Item | `"Item"` |
| Ability or Talent | `"Ability"` |
| Class | `"Class"` |
| Subrace | `"Subrace"` |
| Race (but not Subrace) | `"Race"` |
| Background | `"Background"` |
| Culture | `"Culture"` |
| Deity | `"Deity"` |
| Paladin Order | `"PaladinOrder"` |
| Faction or Reputation | `"Faction"` |
| Disposition Strength | `"DispositionStrength"` |
| Disposition (but not Strength) | `"Disposition"` |
| Skill | `"Skill"` |
| Phrase | `"Phrase"` |
| Keyword | `"Keyword"` |
| Status Effect | `"StatusEffect"` |
| Creature | `"CreatureType"` |
| Map or Area | `"Map"` |
| Conversation | `"Conversation"` |
| Weapon | `"WeaponType"` |
| Armor | `"ArmorType"` |
| Object GUID (scene objects) | *(leave empty — no data source)* |

**`"type": "String"` — only PoE1 game-data references:**
- If the script's `games` array contains only `"poe1"` and the name suggests an item/ability/quest → add the appropriate `"lookupKind"` to match the PoE1 parser (e.g. `"Item"`, `"Ability"`)
- All other `String` params → no `"lookupKind"`

Example annotated parameter:
```json
{
  "name": "Quest GUID",
  "type": "Guid",
  "lookupKind": "Quest",
  "description": "The GUID of the quest to start.",
  "default": ""
}
```

- [ ] **Step 4: Annotate conditions.json** using the same rules.

- [ ] **Step 5: Run whitelist test — expect green**

```
dotnet test "DialogEditor.Tests/DialogEditor.Tests.csproj" --filter "FullyQualifiedName~LookupKindWhitelistTests"
```
Expected: 2 tests pass. If either fails, the failing kind is a typo — fix it in the JSON.

- [ ] **Step 6: Run all tests**

```
dotnet test "DialogEditor.Tests/DialogEditor.Tests.csproj"
```
Expected: all pass.

- [ ] **Step 7: Commit**

```
git add DialogEditor.ViewModels/Resources/scripts.json DialogEditor.ViewModels/Resources/conditions.json DialogEditor.Tests/Services/LookupKindWhitelistTests.cs
git commit -m "feat(params): annotate scripts.json and conditions.json with LookupKind; add whitelist enforcer test"
```

---

## Task 10: Gaps.md update

**Files:**
- Modify: `Gaps.md`

- [ ] **Step 1: Mark the gap implemented**

In `Gaps.md`, find the section:
```
### Parameter Readability — Beyond Characters (PoE2 survey)
```

Replace the header with:
```
### ~~Parameter Readability — Beyond Characters~~ ✓ Implemented
```

Update the body to summarise what shipped and note any PoE1 kinds that could not be sourced (if any were found to have no usable data file during Tasks 7–8). Add a deferred follow-up note for those if needed, following the pattern used by other completed gaps.

- [ ] **Step 2: Commit**

```
git add Gaps.md
git commit -m "docs(gaps): mark Parameter Readability — Beyond Characters implemented"
```
