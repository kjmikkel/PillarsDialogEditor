# VO Path Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add PoE2 voice-over file existence checking — per-node status in the detail panel and a batch validation window under Test → Validate Voice-Over…

**Architecture:** Three new static services (`ChatterPrefixService`, `VoPathResolver`) mirror the existing `SpeakerNameService`/`FlowAnalysisService` pattern. A new `VoValidationViewModel` drives a non-modal window. `NodeDetailViewModel` gains three computed display properties refreshed in `NotifyAllProxies()`. All strings go through `Loc`; no hardcoded UI text.

**Tech Stack:** C# 12, .NET 8, Avalonia UI, CommunityToolkit.Mvvm, xUnit

## Global Constraints

- TDD: write a failing test before any implementation code for every non-trivial method
- Tests run serially (`DialogEditor.Tests` uses `[assembly: CollectionBehavior(DisableTestParallelization = true)]`) — do not re-enable parallelism
- No user-visible strings hardcoded in XAML or C#; all go in `DialogEditor.Avalonia.Shared/Resources/Strings.axaml`
- Every new `Window` must carry `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`
- Every interactive control must have a `ToolTip.Tip` attribute
- Exceptions are logged via `AppLog.Error(...)` or `AppLog.Warn(...)` before any user-facing update; `OperationCanceledException` is swallowed silently; bare `catch { }` is not permitted
- Feature is PoE2-only; silently absent for PoE1 and when no game folder is open
- `Brush.Severity.Error` (red) exists in `Tokens.axaml`; `Brush.Text.Status.Success` (green) exists in `Tokens.axaml` — use these for missing/found respectively

---

## File Map

| File | Action |
|---|---|
| `DialogEditor.Core/Parsing/Poe2SpeakerNameParser.cs` | Extend: add `ParseChatterPrefixes(string json)` + `ParseChatterPrefixesFile(string path)` |
| `DialogEditor.Core/GameData/IGameDataProvider.cs` | Extend: add default `LoadChatterPrefixes()` |
| `DialogEditor.Core/GameData/Poe2GameDataProvider.cs` | Extend: override `LoadChatterPrefixes()` |
| `DialogEditor.ViewModels/Services/ChatterPrefixService.cs` | **Create**: static service, mirrors `SpeakerNameService` |
| `DialogEditor.ViewModels/Services/VoCheckResult.cs` | **Create**: `VoPresence` enum + `VoCheckResult` record |
| `DialogEditor.ViewModels/Services/VoPathResolver.cs` | **Create**: static resolver |
| `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs` | Extend: `GameRoot`, `_voCheck`, derived display props, refresh in `NotifyAllProxies` |
| `DialogEditor.ViewModels/ViewModels/VoValidationViewModel.cs` | **Create**: async scan VM |
| `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` | Extend: `CanValidateVO`, `CreateVoValidationViewModel()`, `ChatterPrefixService.Register`, `Detail.GameRoot` |
| `DialogEditor.Avalonia/Converters/BoolToVoStatusBrushConverter.cs` | **Create**: `true` → `Brush.Text.Status.Success`, `false` → `Brush.Severity.Error` |
| `DialogEditor.Avalonia/App.axaml` | Extend: register `BoolToVoStatusBrushConverter` as `BoolToVoStatusBrush` |
| `DialogEditor.Avalonia/Views/NodeDetailView.axaml` | Extend: VO status `StackPanel` after `HasVO` CheckBox |
| `DialogEditor.Avalonia/Views/VoValidationWindow.axaml` | **Create**: non-modal scan window |
| `DialogEditor.Avalonia/Views/VoValidationWindow.axaml.cs` | **Create**: code-behind |
| `DialogEditor.Avalonia/Views/MainWindow.axaml` | Extend: `Menu_ValidateVO` item in Test menu |
| `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` | Extend: `ValidateVO_Click`, `_voValidationWindow` field |
| `DialogEditor.Avalonia.Shared/Resources/Strings.axaml` | Extend: all new localisation strings |
| `DialogEditor.Tests/Parsing/Poe2SpeakerNameParserTests.cs` | Extend: `ParseChatterPrefixes` tests |
| `DialogEditor.Tests/Services/ChatterPrefixServiceTests.cs` | **Create**: service tests |
| `DialogEditor.Tests/Services/VoPathResolverTests.cs` | **Create**: path construction + ExternalVO tests |
| `DialogEditor.Tests/ViewModels/VoValidationViewModelTests.cs` | **Create**: scan logic, cancellation, summary text |

---

### Task 1: Parser extension + ChatterPrefixService

Extend `Poe2SpeakerNameParser` to read `Components[0].ChatterPrefix`, add the default to `IGameDataProvider`, implement in `Poe2GameDataProvider`, and create the static `ChatterPrefixService`.

**Files:**
- Modify: `DialogEditor.Core/Parsing/Poe2SpeakerNameParser.cs`
- Modify: `DialogEditor.Core/GameData/IGameDataProvider.cs`
- Modify: `DialogEditor.Core/GameData/Poe2GameDataProvider.cs`
- Create: `DialogEditor.ViewModels/Services/ChatterPrefixService.cs`
- Modify: `DialogEditor.Tests/Parsing/Poe2SpeakerNameParserTests.cs`
- Create: `DialogEditor.Tests/Services/ChatterPrefixServiceTests.cs`

**Interfaces:**
- Produces: `Poe2SpeakerNameParser.ParseChatterPrefixes(string json)` → `IReadOnlyDictionary<string, string>` (GUID → ChatterPrefix, case-insensitive)
- Produces: `Poe2SpeakerNameParser.ParseChatterPrefixesFile(string path)` → `IReadOnlyDictionary<string, string>`
- Produces: `IGameDataProvider.LoadChatterPrefixes()` → `IReadOnlyDictionary<string, string>` (default: empty)
- Produces: `ChatterPrefixService.Register(IReadOnlyDictionary<string, string>)`, `ChatterPrefixService.GetPrefix(string guid)` → `string?`, `ChatterPrefixService.Clear()`

- [ ] **Step 1: Write failing tests for `ParseChatterPrefixes`**

Add to `DialogEditor.Tests/Parsing/Poe2SpeakerNameParserTests.cs`:

```csharp
// ── ParseChatterPrefixes ────────────────────────────────────────────────

private static string MakeChatterJson(params (string id, string prefix)[] entries)
{
    var objects = string.Join(",", entries.Select(e =>
        $$$"""
        {"ID":"{{{e.id}}}","DebugName":"SPK_Test","Components":[{"$type":"SpeakerComponent","ChatterPrefix":"{{{e.prefix}}}"}]}
        """));
    return $$$"""{"GameDataObjects":[{{{objects}}}]}""";
}

[Fact]
public void ParseChatterPrefixes_EmptyBundle_ReturnsEmptyDict()
{
    var result = Poe2SpeakerNameParser.ParseChatterPrefixes("""{"GameDataObjects":[]}""");
    Assert.Empty(result);
}

[Fact]
public void ParseChatterPrefixes_SingleEntry_ReturnsPrefixKeyedByGuid()
{
    var json = MakeChatterJson(("9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "eder"));
    var result = Poe2SpeakerNameParser.ParseChatterPrefixes(json);
    Assert.Equal("eder", result["9c5f12c9-e93d-4952-9f1a-726c9498f8fb"]);
}

[Fact]
public void ParseChatterPrefixes_NullOrEmptyPrefix_Skipped()
{
    var objects = """
        {"ID":"aaaa-0000","DebugName":"X","Components":[{"ChatterPrefix":""}]},
        {"ID":"bbbb-0000","DebugName":"Y","Components":[{}]}
        """;
    var json = $$$"""{"GameDataObjects":[{{{objects}}}]}""";
    var result = Poe2SpeakerNameParser.ParseChatterPrefixes(json);
    Assert.Empty(result);
}

[Fact]
public void ParseChatterPrefixes_MultipleEntries_AllPresent()
{
    var json = MakeChatterJson(
        ("9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "eder"),
        ("5529e4b7-42dc-4895-b9f8-23375a945413", "aloth"));
    var result = Poe2SpeakerNameParser.ParseChatterPrefixes(json);
    Assert.Equal(2, result.Count);
    Assert.Equal("eder",  result["9c5f12c9-e93d-4952-9f1a-726c9498f8fb"]);
    Assert.Equal("aloth", result["5529e4b7-42dc-4895-b9f8-23375a945413"]);
}

[Fact]
public void ParseChatterPrefixes_LookupIsCaseInsensitive()
{
    var json = MakeChatterJson(("9C5F12C9-E93D-4952-9F1A-726C9498F8FB", "eder"));
    var result = Poe2SpeakerNameParser.ParseChatterPrefixes(json);
    Assert.True(result.ContainsKey("9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
}

[Fact]
public void ParseChatterPrefixes_EntryWithNoComponents_Skipped()
{
    var json = """{"GameDataObjects":[{"ID":"aaaa-0000","DebugName":"X","Components":[]}]}""";
    var result = Poe2SpeakerNameParser.ParseChatterPrefixes(json);
    Assert.Empty(result);
}
```

- [ ] **Step 2: Run new tests to verify they fail**

```
dotnet test DialogEditor.Tests --filter "ParseChatterPrefixes"
```
Expected: FAIL with `ParseChatterPrefixes` not found.

- [ ] **Step 3: Implement `ParseChatterPrefixes` in `Poe2SpeakerNameParser.cs`**

Add after the existing `ParseFile` method:

```csharp
public static IReadOnlyDictionary<string, string> ParseChatterPrefixes(string json)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var root = JsonNode.Parse(json);
    var objects = root?["GameDataObjects"]?.AsArray();
    if (objects is null) return result;

    foreach (var entry in objects)
    {
        var id = entry?["ID"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id)) continue;

        var components = entry?["Components"]?.AsArray();
        if (components is null || components.Count == 0) continue;

        var prefix = components[0]?["ChatterPrefix"]?.GetValue<string>();
        if (string.IsNullOrEmpty(prefix)) continue;

        result[id] = prefix;
    }

    return result;
}

public static IReadOnlyDictionary<string, string> ParseChatterPrefixesFile(string path)
{
    var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
    if (json.StartsWith('﻿')) json = json[1..];
    return ParseChatterPrefixes(json);
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test DialogEditor.Tests --filter "ParseChatterPrefixes"
```
Expected: all 6 PASS.

- [ ] **Step 5: Write failing tests for `ChatterPrefixService`**

Create `DialogEditor.Tests/Services/ChatterPrefixServiceTests.cs`:

```csharp
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class ChatterPrefixServiceTests : IDisposable
{
    public void Dispose() => ChatterPrefixService.Clear();

    [Fact]
    public void GetPrefix_UnknownGuid_ReturnsNull()
    {
        ChatterPrefixService.Clear();
        Assert.Null(ChatterPrefixService.GetPrefix("00000000-0000-0000-0000-000000000000"));
    }

    [Fact]
    public void Register_ThenGetPrefix_ReturnsPrefix()
    {
        ChatterPrefixService.Register(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "eder" }
        });
        Assert.Equal("eder", ChatterPrefixService.GetPrefix("9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
    }

    [Fact]
    public void Register_LookupIsCaseInsensitive()
    {
        ChatterPrefixService.Register(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "9C5F12C9-E93D-4952-9F1A-726C9498F8FB", "eder" }
        });
        Assert.Equal("eder", ChatterPrefixService.GetPrefix("9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
    }

    [Fact]
    public void Register_Twice_ReplacesData()
    {
        ChatterPrefixService.Register(new Dictionary<string, string> { { "aaa", "old" } });
        ChatterPrefixService.Register(new Dictionary<string, string> { { "bbb", "new" } });
        Assert.Null(ChatterPrefixService.GetPrefix("aaa"));
        Assert.Equal("new", ChatterPrefixService.GetPrefix("bbb"));
    }

    [Fact]
    public void Clear_RemovesAllData()
    {
        ChatterPrefixService.Register(new Dictionary<string, string> { { "aaa", "eder" } });
        ChatterPrefixService.Clear();
        Assert.Null(ChatterPrefixService.GetPrefix("aaa"));
    }
}
```

- [ ] **Step 6: Run to verify they fail**

```
dotnet test DialogEditor.Tests --filter "ChatterPrefixServiceTests"
```
Expected: FAIL — `ChatterPrefixService` not found.

- [ ] **Step 7: Create `ChatterPrefixService.cs`**

Create `DialogEditor.ViewModels/Services/ChatterPrefixService.cs`:

```csharp
namespace DialogEditor.ViewModels.Services;

public static class ChatterPrefixService
{
    private static Dictionary<string, string> _registered =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(IReadOnlyDictionary<string, string> prefixes)
        => _registered = new Dictionary<string, string>(prefixes, StringComparer.OrdinalIgnoreCase);

    public static string? GetPrefix(string speakerGuid)
        => _registered.TryGetValue(speakerGuid, out var prefix) ? prefix : null;

    public static void Clear()
        => _registered = new(StringComparer.OrdinalIgnoreCase);
}
```

- [ ] **Step 8: Add `LoadChatterPrefixes()` default to `IGameDataProvider`**

In `DialogEditor.Core/GameData/IGameDataProvider.cs`, add after `LoadSpeakerNames()`:

```csharp
/// Returns a GUID → ChatterPrefix map for PoE2. Default returns empty; PoE1 unaffected.
IReadOnlyDictionary<string, string> LoadChatterPrefixes() => new Dictionary<string, string>();
```

- [ ] **Step 9: Override `LoadChatterPrefixes()` in `Poe2GameDataProvider`**

In `DialogEditor.Core/GameData/Poe2GameDataProvider.cs`, add after `LoadSpeakerNames()`:

```csharp
public IReadOnlyDictionary<string, string> LoadChatterPrefixes() =>
    File.Exists(SpeakersBundle)
        ? Poe2SpeakerNameParser.ParseChatterPrefixesFile(SpeakersBundle)
        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
```

- [ ] **Step 10: Run all ChatterPrefix tests to verify they pass**

```
dotnet test DialogEditor.Tests --filter "ParseChatterPrefixes|ChatterPrefixServiceTests"
```
Expected: all 11 PASS.

- [ ] **Step 11: Commit**

```
git add DialogEditor.Core/Parsing/Poe2SpeakerNameParser.cs \
        DialogEditor.Core/GameData/IGameDataProvider.cs \
        DialogEditor.Core/GameData/Poe2GameDataProvider.cs \
        DialogEditor.ViewModels/Services/ChatterPrefixService.cs \
        DialogEditor.Tests/Parsing/Poe2SpeakerNameParserTests.cs \
        DialogEditor.Tests/Services/ChatterPrefixServiceTests.cs
git commit -m "feat(vo): add ChatterPrefix parsing and ChatterPrefixService"
```

---

### Task 2: VoCheckResult + VoPathResolver

Define the result types and the static resolver that checks file existence. Both live in `DialogEditor.ViewModels.Services` because `VoPathResolver` depends on `ChatterPrefixService` — pulling them into Core would create a circular reference.

**Files:**
- Create: `DialogEditor.ViewModels/Services/VoCheckResult.cs`
- Create: `DialogEditor.ViewModels/Services/VoPathResolver.cs`
- Create: `DialogEditor.Tests/Services/VoPathResolverTests.cs`

**Interfaces:**
- Consumes: `ChatterPrefixService.GetPrefix(string guid)` (Task 1)
- Produces: `VoPresence` enum (`NotApplicable`, `Found`, `Missing`)
- Produces: `VoCheckResult` record `(VoPresence Status, bool FemaleVariantFound)`
- Produces: `VoPathResolver.Check(string speakerGuid, bool hasVO, string externalVO, int nodeId, string conversationName, string gameRoot, string activeGameId)` → `VoCheckResult?`

- [ ] **Step 1: Write failing tests for `VoPathResolver`**

Create `DialogEditor.Tests/Services/VoPathResolverTests.cs`:

```csharp
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class VoPathResolverTests : IDisposable
{
    // Use a temp directory so tests can plant/remove real files
    private readonly string _voRoot;
    private readonly string _gameRoot;

    public VoPathResolverTests()
    {
        _gameRoot = Path.Combine(Path.GetTempPath(), $"VoTest_{Guid.NewGuid():N}");
        _voRoot   = Path.Combine(_gameRoot,
            "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
        Directory.CreateDirectory(_voRoot);

        ChatterPrefixService.Register(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "eder" }
        });
    }

    public void Dispose()
    {
        ChatterPrefixService.Clear();
        if (Directory.Exists(_gameRoot))
            Directory.Delete(_gameRoot, recursive: true);
    }

    // ── Returns null for non-PoE2 ─────────────────────────────────────────

    [Fact]
    public void Check_Poe1GameId_ReturnsNull()
    {
        var result = VoPathResolver.Check("any-guid", true, "", 1, "conv", _gameRoot, "poe1");
        Assert.Null(result);
    }

    [Fact]
    public void Check_EmptyGameRoot_ReturnsNull()
    {
        var result = VoPathResolver.Check("any-guid", true, "", 1, "conv", "", "poe2");
        Assert.Null(result);
    }

    // ── NotApplicable ─────────────────────────────────────────────────────

    [Fact]
    public void Check_NoHasVO_NoExternalVO_ReturnsNotApplicable()
    {
        var result = VoPathResolver.Check("any-guid", false, "", 1, "conv", _gameRoot, "poe2");
        Assert.NotNull(result);
        Assert.Equal(VoPresence.NotApplicable, result!.Status);
    }

    // ── Missing (unknown speaker) ─────────────────────────────────────────

    [Fact]
    public void Check_HasVO_UnknownSpeaker_ReturnsMissing()
    {
        var result = VoPathResolver.Check("unknown-guid", true, "", 1, "conv", _gameRoot, "poe2");
        Assert.Equal(VoPresence.Missing, result!.Status);
    }

    // ── Narrator hardcoded path ───────────────────────────────────────────

    [Fact]
    public void Check_NarratorGuid_UseNarratorPrefix()
    {
        var narratorGuid = "6a99a109-0000-0000-0000-000000000000";
        // File does not exist — result should be Missing (not null, not NotApplicable)
        var result = VoPathResolver.Check(narratorGuid, true, "", 5, "test_conv", _gameRoot, "poe2");
        Assert.Equal(VoPresence.Missing, result!.Status);
    }

    [Fact]
    public void Check_NarratorGuid_FileExists_ReturnsFound()
    {
        var narratorGuid = "6a99a109-0000-0000-0000-000000000000";
        var dir  = Path.Combine(_voRoot, "narrator");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0005.wem"), "");

        var result = VoPathResolver.Check(narratorGuid, true, "", 5, "test_conv", _gameRoot, "poe2");

        Assert.Equal(VoPresence.Found, result!.Status);
        Assert.False(result.FemaleVariantFound);
    }

    // ── Standard path ─────────────────────────────────────────────────────

    [Fact]
    public void Check_HasVO_KnownSpeaker_FileExists_ReturnsFound()
    {
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0001.wem"), "");

        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "test_conv", _gameRoot, "poe2");

        Assert.Equal(VoPresence.Found, result!.Status);
        Assert.False(result.FemaleVariantFound);
    }

    [Fact]
    public void Check_HasVO_KnownSpeaker_FileMissing_ReturnsMissing()
    {
        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "test_conv", _gameRoot, "poe2");
        Assert.Equal(VoPresence.Missing, result!.Status);
    }

    [Fact]
    public void Check_FemVariantAlsoExists_FemaleVariantFoundIsTrue()
    {
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0001.wem"),     "");
        File.WriteAllText(Path.Combine(dir, "test_conv_0001_fem.wem"), "");

        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "test_conv", _gameRoot, "poe2");

        Assert.Equal(VoPresence.Found, result!.Status);
        Assert.True(result.FemaleVariantFound);
    }

    [Fact]
    public void Check_NodeIdPaddedToFourDigits()
    {
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0042.wem"), "");

        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 42, "test_conv", _gameRoot, "poe2");

        Assert.Equal(VoPresence.Found, result!.Status);
    }

    [Fact]
    public void Check_ConvNameLowercased()
    {
        // File uses lowercase name; conversationName passed in mixed case
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "my_conv_0001.wem"), "");

        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "My_Conv", _gameRoot, "poe2");

        Assert.Equal(VoPresence.Found, result!.Status);
    }

    // ── ExternalVO override ───────────────────────────────────────────────

    [Fact]
    public void Check_ExternalVO_OverridesStandardPath()
    {
        // ExternalVO = "eder/00_cv_test_0153" → looks for .../eder/00_cv_test_0153.wem
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "00_cv_test_0153.wem"), "");

        var result = VoPathResolver.Check(
            "unknown-guid", false, "eder/00_cv_test_0153", 999, "anything", _gameRoot, "poe2");

        Assert.Equal(VoPresence.Found, result!.Status);
    }

    [Fact]
    public void Check_ExternalVO_FileAbsent_ReturnsMissing()
    {
        var result = VoPathResolver.Check(
            "any-guid", false, "eder/00_cv_missing_0001", 1, "conv", _gameRoot, "poe2");
        Assert.Equal(VoPresence.Missing, result!.Status);
    }

    [Fact]
    public void Check_ExternalVO_TakesPrecedenceOverHasVO()
    {
        // HasVO AND ExternalVO set: ExternalVO path is used
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "override_0001.wem"), "");

        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb",
            hasVO: true,
            externalVO: "eder/override_0001",
            nodeId: 99, conversationName: "conv",
            _gameRoot, "poe2");

        Assert.Equal(VoPresence.Found, result!.Status);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test DialogEditor.Tests --filter "VoPathResolverTests"
```
Expected: FAIL — types not found.

- [ ] **Step 3: Create `VoCheckResult.cs`**

Create `DialogEditor.ViewModels/Services/VoCheckResult.cs`:

```csharp
namespace DialogEditor.ViewModels.Services;

public enum VoPresence { NotApplicable, Found, Missing }

public record VoCheckResult(VoPresence Status, bool FemaleVariantFound);
```

- [ ] **Step 4: Create `VoPathResolver.cs`**

Create `DialogEditor.ViewModels/Services/VoPathResolver.cs`:

```csharp
namespace DialogEditor.ViewModels.Services;

public static class VoPathResolver
{
    private const string NarratorGuid = "6a99a109-0000-0000-0000-000000000000";

    /// Returns null when the feature does not apply (non-PoE2 or no game folder).
    /// Returns VoCheckResult(NotApplicable) when the node has neither HasVO nor ExternalVO.
    public static VoCheckResult? Check(
        string speakerGuid,
        bool   hasVO,
        string externalVO,
        int    nodeId,
        string conversationName,
        string gameRoot,
        string activeGameId)
    {
        if (!string.Equals(activeGameId, "poe2", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(gameRoot))
            return null;

        if (!hasVO && string.IsNullOrEmpty(externalVO))
            return new VoCheckResult(VoPresence.NotApplicable, false);

        var voRoot = Path.Combine(gameRoot,
            "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");

        string basePath;
        if (!string.IsNullOrEmpty(externalVO))
        {
            // ExternalVO ships with '/' separators; Path.Combine normalises on Windows.
            basePath = Path.Combine(voRoot, externalVO);
        }
        else
        {
            var chatterPrefix = string.Equals(speakerGuid, NarratorGuid,
                                    StringComparison.OrdinalIgnoreCase)
                                ? "narrator"
                                : ChatterPrefixService.GetPrefix(speakerGuid);

            if (string.IsNullOrEmpty(chatterPrefix))
                return new VoCheckResult(VoPresence.Missing, false);

            basePath = Path.Combine(voRoot,
                chatterPrefix.ToLowerInvariant(),
                $"{conversationName.ToLowerInvariant()}_{nodeId:0000}");
        }

        var primary = basePath + ".wem";
        var fem     = basePath + "_fem.wem";
        return new VoCheckResult(
            File.Exists(primary) ? VoPresence.Found : VoPresence.Missing,
            File.Exists(fem));
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```
dotnet test DialogEditor.Tests --filter "VoPathResolverTests"
```
Expected: all 14 PASS.

- [ ] **Step 6: Commit**

```
git add DialogEditor.ViewModels/Services/VoCheckResult.cs \
        DialogEditor.ViewModels/Services/VoPathResolver.cs \
        DialogEditor.Tests/Services/VoPathResolverTests.cs
git commit -m "feat(vo): add VoCheckResult types and VoPathResolver"
```

---

### Task 3: Per-node status — ViewModel + Converter + View + Strings

Add the inline VO status row to the node detail panel.

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`
- Create: `DialogEditor.Avalonia/Converters/BoolToVoStatusBrushConverter.cs`
- Modify: `DialogEditor.Avalonia/App.axaml`
- Modify: `DialogEditor.Avalonia/Views/NodeDetailView.axaml`
- Modify: `DialogEditor.Avalonia.Shared/Resources/Strings.axaml`

**Interfaces:**
- Consumes: `VoPathResolver.Check(...)` (Task 2), `ChatterPrefixService.GetPrefix` (Task 1)
- Produces: `NodeDetailViewModel.GameRoot { get; set; }`, `HasVoStatus`, `VoStatusGlyph`, `VoStatusText`, `VoStatusIsFound`

- [ ] **Step 1: Add VO strings to `Strings.axaml`**

In `DialogEditor.Avalonia.Shared/Resources/Strings.axaml`, add in the localisation section (after the existing VO-related strings if any, otherwise near end):

```xml
<!-- ── Voice-Over validation ───────────────────────────────────────── -->
<x:String x:Key="VoStatus_Found">VO file found</x:String>
<x:String x:Key="VoStatus_FoundWithFem">VO file found (+ female variant)</x:String>
<x:String x:Key="VoStatus_Missing">VO file missing</x:String>
<x:String x:Key="VoValidation_Title">Voice-Over Validation</x:String>
<x:String x:Key="VoValidation_Running">Checking…</x:String>
<x:String x:Key="VoValidation_Summary">Checked {0} nodes — {1} missing</x:String>
<x:String x:Key="VoValidation_AllFound">All voice-over files found.</x:String>
<x:String x:Key="VoValidation_Cancelled">Cancelled. Checked {0} nodes — {1} missing so far.</x:String>
<x:String x:Key="Menu_ValidateVO">Validate Voice-Over…</x:String>
<x:String x:Key="ToolTip_ValidateVO">Scan the open conversation for nodes that are flagged as voiced (HasVO or ExternalVO set) but whose audio file could not be found on disk. Only available for Pillars of Eternity II with a game folder open.</x:String>
<x:String x:Key="Button_RunAgain">Run Again</x:String>
<x:String x:Key="VoValidation_NodeRow">Node {0}</x:String>
<x:String x:Key="VoValidation_MissingBadge">✗ Missing</x:String>
```

- [ ] **Step 2: Add VO display properties and `GameRoot` to `NodeDetailViewModel`**

In `NodeDetailViewModel.cs`:

After the `ActiveGameId` property (line 20), add:

```csharp
/// Set by MainWindowViewModel when a game folder is opened.
public string GameRoot { get; set; } = string.Empty;
```

After the `HasVO` property block (~line 126), add the backing field and computed properties:

```csharp
// ── VO file status (PoE2 only) ────────────────────────────────────
private VoCheckResult? _voCheck;

public bool HasVoStatus     => _voCheck is { Status: not VoPresence.NotApplicable };
public bool VoStatusIsFound => _voCheck?.Status == VoPresence.Found;

public string VoStatusGlyph => _voCheck?.Status == VoPresence.Found ? "✓" : "✗";

public string VoStatusText => _voCheck switch
{
    { Status: VoPresence.Found, FemaleVariantFound: true }  => Loc.Get("VoStatus_FoundWithFem"),
    { Status: VoPresence.Found, FemaleVariantFound: false } => Loc.Get("VoStatus_Found"),
    _ => Loc.Get("VoStatus_Missing"),
};
```

In `NotifyAllProxies()`, after the `OnPropertyChanged(nameof(BarkWarnings))` call, add:

```csharp
_voCheck = _node is null ? null
    : VoPathResolver.Check(
        _node.SpeakerGuid, _node.HasVO, _node.ExternalVO, _node.NodeId,
        Canvas?.ConversationName ?? "", GameRoot, ActiveGameId);
OnPropertyChanged(nameof(HasVoStatus));
OnPropertyChanged(nameof(VoStatusGlyph));
OnPropertyChanged(nameof(VoStatusText));
OnPropertyChanged(nameof(VoStatusIsFound));
```

Make sure `using DialogEditor.ViewModels.Services;` is in the using list at the top.

- [ ] **Step 3: Create `BoolToVoStatusBrushConverter.cs`**

Create `DialogEditor.Avalonia/Converters/BoolToVoStatusBrushConverter.cs`:

```csharp
using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.Avalonia.Theming;

namespace DialogEditor.Avalonia.Converters;

public sealed class BoolToVoStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? TokenBrushes.Resolve("Brush.Text.Status.Success")
            : TokenBrushes.Resolve("Brush.Severity.Error");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

- [ ] **Step 4: Register the converter in `App.axaml`**

In `DialogEditor.Avalonia/App.axaml`, after the `BoolToFemaleTextBrushConverter` line (line ~97):

```xml
<converters:BoolToVoStatusBrushConverter x:Key="BoolToVoStatusBrush"/>
```

- [ ] **Step 5: Add VO status row to `NodeDetailView.axaml`**

In `DialogEditor.Avalonia/Views/NodeDetailView.axaml`, after the `HasVO` CheckBox (after line 204) and before the `HideSpeaker` CheckBox:

```xml
<!-- VO file status — PoE2 only; shown when HasVO or ExternalVO set -->
<StackPanel Orientation="Horizontal" Margin="0,2,0,4"
            IsVisible="{Binding HasVoStatus}"
            ToolTip.Tip="{Binding VoStatusText}"
            AutomationProperties.HelpText="{Binding VoStatusText}">
    <TextBlock Text="{Binding VoStatusGlyph}"
               Foreground="{Binding VoStatusIsFound, Converter={StaticResource BoolToVoStatusBrush}}"
               FontSize="{DynamicResource FontSize.Small}"
               VerticalAlignment="Center" Margin="0,0,4,0"/>
    <TextBlock Text="{Binding VoStatusText}"
               Foreground="{Binding VoStatusIsFound, Converter={StaticResource BoolToVoStatusBrush}}"
               FontSize="{DynamicResource FontSize.Small}"
               VerticalAlignment="Center"/>
</StackPanel>
```

- [ ] **Step 6: Build to verify no compilation errors**

```
dotnet build DialogEditor.Avalonia
```
Expected: 0 errors.

- [ ] **Step 7: Commit**

```
git add DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs \
        DialogEditor.Avalonia/Converters/BoolToVoStatusBrushConverter.cs \
        DialogEditor.Avalonia/App.axaml \
        DialogEditor.Avalonia/Views/NodeDetailView.axaml \
        "DialogEditor.Avalonia.Shared/Resources/Strings.axaml"
git commit -m "feat(vo): add per-node VO status indicator in node detail panel"
```

---

### Task 4: VoValidationViewModel

Async scan ViewModel with cancellation support and summary text.

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/VoValidationViewModel.cs`
- Create: `DialogEditor.Tests/ViewModels/VoValidationViewModelTests.cs`

**Interfaces:**
- Consumes: `VoPathResolver.Check(...)` (Task 2), `NodeEditSnapshot` (Core Editing), `Loc.Format` / `Loc.Get`
- Produces: `VoValidationIssue` record, `VoValidationViewModel` class
- `VoValidationViewModel` constructor: `(IReadOnlyList<NodeEditSnapshot> nodes, string conversationName, string gameRoot, string activeGameId)`
- Produces: `bool IsRunning`, `ObservableCollection<VoValidationIssue> Results`, `string SummaryText`, `IRelayCommand CancelCommand`, `IRelayCommand RunAgainCommand`
- `RunAsync()` is the entry point called from `MainWindow.axaml.cs`

- [ ] **Step 1: Write failing tests**

Create `DialogEditor.Tests/ViewModels/VoValidationViewModelTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class VoValidationViewModelTests : IDisposable
{
    private readonly string _gameRoot;
    private readonly string _voRoot;

    public VoValidationViewModelTests()
    {
        Loc.Configure(new StubStringProvider());
        _gameRoot = Path.Combine(Path.GetTempPath(), $"VoVmTest_{Guid.NewGuid():N}");
        _voRoot   = Path.Combine(_gameRoot,
            "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
        Directory.CreateDirectory(_voRoot);

        ChatterPrefixService.Register(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "eder" }
        });
    }

    public void Dispose()
    {
        ChatterPrefixService.Clear();
        if (Directory.Exists(_gameRoot))
            Directory.Delete(_gameRoot, recursive: true);
    }

    private static NodeEditSnapshot MakeNode(
        int id,
        string speakerGuid = "9c5f12c9-e93d-4952-9f1a-726c9498f8fb",
        bool hasVO = false,
        string externalVO = "",
        string defaultText = "Test text") =>
        new(id, false, SpeakerCategory.Npc, speakerGuid, "",
            defaultText, "", "Conversation", "None", "", "", externalVO, hasVO, false, [], [], []);

    [Fact]
    public async Task RunAsync_NoVoNodes_ResultsEmpty_SummaryShowsZero()
    {
        var nodes = new[] { MakeNode(1, hasVO: false) };
        var vm = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe2");

        await vm.RunAsync();

        Assert.Empty(vm.Results);
        Assert.Contains("0", vm.SummaryText);
    }

    [Fact]
    public async Task RunAsync_HasVO_FileMissing_AddsIssue()
    {
        var nodes = new[] { MakeNode(1, hasVO: true) };
        var vm = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe2");

        await vm.RunAsync();

        Assert.Single(vm.Results);
        Assert.True(vm.Results[0].IsMissing);
        Assert.Equal(1, vm.Results[0].NodeId);
    }

    [Fact]
    public async Task RunAsync_HasVO_FileExists_NoIssueAdded()
    {
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0001.wem"), "");

        var nodes = new[] { MakeNode(1, hasVO: true) };
        var vm = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe2");

        await vm.RunAsync();

        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task RunAsync_SummaryText_ReflectsCheckedAndMissingCounts()
    {
        // Node 1: file present, node 2: file missing
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0001.wem"), "");

        var nodes = new[] { MakeNode(1, hasVO: true), MakeNode(2, hasVO: true) };
        var vm = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe2");

        await vm.RunAsync();

        // Should mention 2 checked, 1 missing
        Assert.Contains("2", vm.SummaryText);
        Assert.Contains("1", vm.SummaryText);
    }

    [Fact]
    public async Task RunAsync_TextPreviewTruncatedAt60Chars()
    {
        var long70 = new string('x', 70);
        var nodes  = new[] { MakeNode(1, hasVO: true, defaultText: long70) };
        var vm     = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe2");

        await vm.RunAsync();

        Assert.Single(vm.Results);
        Assert.True(vm.Results[0].TextPreview.Length <= 63); // 60 chars + "…"
        Assert.EndsWith("…", vm.Results[0].TextPreview);
    }

    [Fact]
    public async Task RunAsync_IsRunningFalseAfterCompletion()
    {
        var nodes = new[] { MakeNode(1, hasVO: true) };
        var vm = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe2");

        await vm.RunAsync();

        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task RunAgainCommand_ClearsResultsAndReruns()
    {
        var nodes = new[] { MakeNode(1, hasVO: true) };
        var vm = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe2");

        await vm.RunAsync(); // first run — 1 missing
        Assert.Single(vm.Results);

        // Plant the file so second run finds it
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0001.wem"), "");

        await vm.RunAsync(); // second run
        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task RunAsync_Poe1GameId_ResultsEmpty()
    {
        var nodes = new[] { MakeNode(1, hasVO: true) };
        var vm = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe1");

        await vm.RunAsync();

        Assert.Empty(vm.Results);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

```
dotnet test DialogEditor.Tests --filter "VoValidationViewModelTests"
```
Expected: FAIL — `VoValidationViewModel` not found.

- [ ] **Step 3: Create `VoValidationViewModel.cs`**

Create `DialogEditor.ViewModels/ViewModels/VoValidationViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public record VoValidationIssue(int NodeId, string TextPreview, bool IsMissing);

public partial class VoValidationViewModel : ObservableObject
{
    private readonly IReadOnlyList<NodeEditSnapshot> _nodes;
    private readonly string _conversationName;
    private readonly string _gameRoot;
    private readonly string _activeGameId;

    private CancellationTokenSource _cts = new();

    [ObservableProperty] private bool   _isRunning;
    [ObservableProperty] private string _summaryText = string.Empty;

    public ObservableCollection<VoValidationIssue> Results { get; } = [];

    public IRelayCommand CancelCommand  { get; }
    public IRelayCommand RunAgainCommand { get; }

    public VoValidationViewModel(
        IReadOnlyList<NodeEditSnapshot> nodes,
        string conversationName,
        string gameRoot,
        string activeGameId)
    {
        _nodes            = nodes;
        _conversationName = conversationName;
        _gameRoot         = gameRoot;
        _activeGameId     = activeGameId;

        CancelCommand   = new RelayCommand(() => _cts.Cancel(), () => IsRunning);
        RunAgainCommand = new RelayCommand(async () => await RunAsync(), () => !IsRunning);
    }

    partial void OnIsRunningChanged(bool value)
    {
        CancelCommand.NotifyCanExecuteChanged();
        RunAgainCommand.NotifyCanExecuteChanged();
    }

    public async Task RunAsync()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Results.Clear();
        IsRunning   = true;
        SummaryText = Loc.Get("VoValidation_Running");

        var checked_ = 0;
        var missing  = 0;
        var cancelled = false;

        try
        {
            await Task.Run(() =>
            {
                foreach (var node in _nodes)
                {
                    token.ThrowIfCancellationRequested();

                    var result = VoPathResolver.Check(
                        node.SpeakerGuid, node.HasVO, node.ExternalVO, node.NodeId,
                        _conversationName, _gameRoot, _activeGameId);

                    if (result is null || result.Status == VoPresence.NotApplicable)
                        continue;

                    checked_++;
                    if (result.Status == VoPresence.Missing)
                    {
                        missing++;
                        var preview = BuildPreview(node.DefaultText);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            Results.Add(new VoValidationIssue(node.NodeId, preview, IsMissing: true)));
                    }
                }
            }, token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        IsRunning = false;
        SummaryText = cancelled
            ? Loc.Format("VoValidation_Cancelled", checked_, missing)
            : Loc.Format("VoValidation_Summary",   checked_, missing);
    }

    private static string BuildPreview(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 60 ? trimmed : trimmed[..60] + "…";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test DialogEditor.Tests --filter "VoValidationViewModelTests"
```
Expected: all 9 PASS.

- [ ] **Step 5: Commit**

```
git add DialogEditor.ViewModels/ViewModels/VoValidationViewModel.cs \
        DialogEditor.Tests/ViewModels/VoValidationViewModelTests.cs
git commit -m "feat(vo): add VoValidationViewModel with async scan and cancellation"
```

---

### Task 5: VoValidationWindow + MainWindow wiring + MainWindowViewModel

Wire up the UI: the new validation window, the Test menu item, and `MainWindowViewModel` registration of `ChatterPrefixService` and `Detail.GameRoot`.

**Files:**
- Create: `DialogEditor.Avalonia/Views/VoValidationWindow.axaml`
- Create: `DialogEditor.Avalonia/Views/VoValidationWindow.axaml.cs`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`

**Interfaces:**
- Consumes: `VoValidationViewModel` (Task 4), `ChatterPrefixService.Register` (Task 1)
- Produces: `MainWindowViewModel.CanValidateVO`, `MainWindowViewModel.CreateVoValidationViewModel()`

- [ ] **Step 1: Create `VoValidationWindow.axaml`**

Create `DialogEditor.Avalonia/Views/VoValidationWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"
        x:Class="DialogEditor.Avalonia.Views.VoValidationWindow"
        Title="{DynamicResource VoValidation_Title}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="500" Height="420" MinWidth="420" MinHeight="280"
        CanResize="True"
        ShowInTaskbar="False"
        Background="{DynamicResource Brush.Surface.Window}"
        WindowStartupLocation="CenterOwner"
        x:CompileBindings="False">

    <Grid RowDefinitions="Auto,Auto,Auto,*,Auto" Margin="14,12,14,12">

        <!-- Summary text -->
        <TextBlock Grid.Row="0"
                   Text="{Binding SummaryText}"
                   Foreground="{DynamicResource Brush.Text.Primary}"
                   FontSize="{DynamicResource FontSize.Body}"
                   Margin="0,0,0,6"/>

        <!-- Progress bar (visible while running) -->
        <ProgressBar Grid.Row="1"
                     IsIndeterminate="True"
                     IsVisible="{Binding IsRunning}"
                     Height="4" Margin="0,0,0,8"/>

        <!-- Button row -->
        <StackPanel Grid.Row="2" Orientation="Horizontal" Spacing="8" Margin="0,0,0,8">
            <Button Content="{DynamicResource Button_Cancel}"
                    Command="{Binding CancelCommand}"
                    IsVisible="{Binding IsRunning}"
                    ToolTip.Tip="{DynamicResource Button_Cancel}"
                    AutomationProperties.HelpText="{DynamicResource Button_Cancel}"/>
            <Button Content="{DynamicResource Button_RunAgain}"
                    Command="{Binding RunAgainCommand}"
                    IsVisible="{Binding IsRunning, Converter={StaticResource InverseBoolToVis}}"
                    ToolTip.Tip="{DynamicResource Button_RunAgain}"
                    AutomationProperties.HelpText="{DynamicResource Button_RunAgain}"/>
        </StackPanel>

        <!-- Results list -->
        <ScrollViewer Grid.Row="3" Margin="0,0,0,6">
            <Panel>
                <!-- Empty-state message after a completed scan with no issues -->
                <TextBlock Text="{DynamicResource VoValidation_AllFound}"
                           Foreground="{DynamicResource Brush.Text.Disabled}"
                           FontStyle="Italic"
                           HorizontalAlignment="Center" VerticalAlignment="Center"
                           IsVisible="{Binding Results.Count, Converter={StaticResource CountToVis}, ConverterParameter=0}"/>

                <ItemsControl ItemsSource="{Binding Results}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="vm:VoValidationIssue">
                            <Grid ColumnDefinitions="Auto,*,Auto" Margin="0,3" Height="28">
                                <!-- Node ID -->
                                <TextBlock Grid.Column="0"
                                           Text="{Binding NodeId, StringFormat='Node {0}'}"
                                           Foreground="{DynamicResource Brush.Text.Muted}"
                                           FontSize="{DynamicResource FontSize.Small}"
                                           Width="70" VerticalAlignment="Center"/>
                                <!-- Text preview -->
                                <TextBlock Grid.Column="1"
                                           Text="{Binding TextPreview}"
                                           Foreground="{DynamicResource Brush.Text.Secondary}"
                                           FontSize="{DynamicResource FontSize.Label}"
                                           TextTrimming="CharacterEllipsis"
                                           VerticalAlignment="Center"/>
                                <!-- Missing badge -->
                                <TextBlock Grid.Column="2"
                                           Text="{DynamicResource VoValidation_MissingBadge}"
                                           Foreground="{DynamicResource Brush.Severity.Error}"
                                           FontSize="{DynamicResource FontSize.Small}"
                                           FontWeight="Bold"
                                           VerticalAlignment="Center" Margin="8,0,0,0"/>
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </Panel>
        </ScrollViewer>

        <!-- Focus hint bar -->
        <shared:FocusHintBar Grid.Row="4" x:Name="HintBar"/>

    </Grid>
</Window>
```

- [ ] **Step 2: Create `VoValidationWindow.axaml.cs`**

Create `DialogEditor.Avalonia/Views/VoValidationWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class VoValidationWindow : Window
{
    public VoValidationWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public VoValidationWindow(VoValidationViewModel vm) : this()
    {
        DataContext = vm;
    }
}
```

- [ ] **Step 3: Add `Menu_ValidateVO` to `MainWindow.axaml`**

In `DialogEditor.Avalonia/Views/MainWindow.axaml`, in the Test menu, after the `RestoreBackup` MenuItem (before the closing `</MenuItem>` of `Menu_Test`):

```xml
<Separator/>
<MenuItem Header="{DynamicResource Menu_ValidateVO}"
          Click="ValidateVO_Click"
          IsEnabled="{Binding CanValidateVO}"
          ToolTip.Tip="{DynamicResource ToolTip_ValidateVO}"
          AutomationProperties.HelpText="{DynamicResource ToolTip_ValidateVO}"/>
```

- [ ] **Step 4: Add `_voValidationWindow` field and `ValidateVO_Click` to `MainWindow.axaml.cs`**

In `MainWindow.axaml.cs`:

After the `_flowAnalyticsWindow` field declaration (line ~29):

```csharp
private VoValidationWindow? _voValidationWindow;
```

After the `FlowAnalytics_Click` method, add:

```csharp
private void ValidateVO_Click(object? sender, RoutedEventArgs e)
{
    var vm = ((MainWindowViewModel)DataContext!).CreateVoValidationViewModel();
    if (vm is null) return;

    _voValidationWindow = new VoValidationWindow(vm);
    _voValidationWindow.Closed += (_, _) => _voValidationWindow = null;
    _voValidationWindow.Show(this);
    _ = vm.RunAsync();
}
```

- [ ] **Step 5: Add `CanValidateVO`, `CreateVoValidationViewModel()`, and service wiring to `MainWindowViewModel`**

In `MainWindowViewModel.cs`:

Add `CanValidateVO` property (near the `IsProjectOpen` property, ~line 209):

```csharp
public bool CanValidateVO =>
    !string.IsNullOrEmpty(_currentGameDirectory)
    && string.Equals(_activeGameId, "poe2", StringComparison.OrdinalIgnoreCase)
    && Canvas.Nodes.Count > 0;
```

In `LoadDirectory()`, after `Detail.ActiveGameId = provider.GameId;` (line ~1039):

```csharp
Detail.GameRoot = path;
ChatterPrefixService.Register(provider.LoadChatterPrefixes());
OnPropertyChanged(nameof(CanValidateVO));
```

Add `CreateVoValidationViewModel()` method (near other factory methods in the file):

```csharp
public VoValidationViewModel? CreateVoValidationViewModel()
{
    if (!CanValidateVO) return null;
    var snapshot = Canvas.BuildSnapshot();
    return new VoValidationViewModel(
        snapshot.Nodes, Canvas.ConversationName,
        _currentGameDirectory, _activeGameId);
}
```

Also add `OnPropertyChanged(nameof(CanValidateVO));` inside `LoadConversationFile` after a conversation loads, so the menu item enables correctly when a conversation is opened. Find where `Canvas.Load(...)` or similar is called after loading a conversation and add the notification there.

Make sure `using DialogEditor.ViewModels.Services;` is in the using list if not already present.

- [ ] **Step 6: Build to verify no errors**

```
dotnet build DialogEditor.Avalonia
```
Expected: 0 errors.

- [ ] **Step 7: Run the full test suite**

```
dotnet test DialogEditor.Tests
```
Expected: all existing tests pass, all new tests pass.

- [ ] **Step 8: Commit**

```
git add DialogEditor.Avalonia/Views/VoValidationWindow.axaml \
        DialogEditor.Avalonia/Views/VoValidationWindow.axaml.cs \
        DialogEditor.Avalonia/Views/MainWindow.axaml \
        DialogEditor.Avalonia/Views/MainWindow.axaml.cs \
        DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git commit -m "feat(vo): add VoValidationWindow and wire Test menu + MainWindowViewModel"
```

---

## Spec Coverage Check

| Spec section | Task |
|---|---|
| §1 Parser — `ParseChatterPrefixes` / `ParseChatterPrefixesFile` | Task 1 |
| §1 `IGameDataProvider.LoadChatterPrefixes()` default | Task 1 |
| §1 `Poe2GameDataProvider.LoadChatterPrefixes()` | Task 1 |
| §1 `ChatterPrefixService` static service | Task 1 |
| §1 Registration in `MainWindowViewModel.LoadDirectory` | Task 5 |
| §2 `VoPresence` enum + `VoCheckResult` record | Task 2 |
| §2 `VoPathResolver.Check(...)` — all path cases | Task 2 |
| §2 ExternalVO override path | Task 2 |
| §2 Narrator GUID hardcoded to `"narrator"` | Task 2 |
| §2 `_fem.wem` detection (FemaleVariantFound) | Task 2 |
| §3 `NodeDetailViewModel.GameRoot` + `_voCheck` + derived props | Task 3 |
| §3 Refresh in `NotifyAllProxies()` | Task 3 |
| §3 `BoolToVoStatusBrushConverter` | Task 3 |
| §3 VO status row in `NodeDetailView.axaml` | Task 3 |
| §4 `VoValidationIssue` record | Task 4 |
| §4 `VoValidationViewModel` scan + cancel + RunAgain | Task 4 |
| §4 Text preview 60-char truncation | Task 4 |
| §4 `VoValidationWindow` non-modal | Task 5 |
| §4 `CanValidateVO` on `MainWindowViewModel` | Task 5 |
| §4 `ValidateVO_Click` in `MainWindow.axaml.cs` | Task 5 |
| All localisation strings | Task 3 (strings added once, used by Tasks 4 & 5) |
| Window icon rule | Task 5 (`VoValidationWindow.axaml` has the icon) |
| All controls have ToolTips | Tasks 3, 5 |
