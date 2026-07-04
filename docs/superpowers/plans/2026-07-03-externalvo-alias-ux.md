# ExternalVO Alias UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the PoE2 `ExternalVO` VO-alias visible and safely editable: readable alias line + shared-count in the Voice group, node-picker editing (raw path readonly), and an overwrite guard on import, per `docs/superpowers/specs/2026-07-03-externalvo-alias-ux-design.md`.

**Architecture:** A pure parser (`VoAliasParse`, Core) and a session-static disk index (`VoAliasIndexService`, ViewModels) feed new `NodeDetailViewModel` alias properties; a new picker window writes aliases through the existing undoable `NodeViewModel.ExternalVO` property; the single-node import flow gains a three-way confirm delegate; batch import excludes aliased rows.

**Tech Stack:** Avalonia 11, CommunityToolkit.Mvvm, `System.Text.Json` (`JsonDocument`), xUnit (serial suite; `StubStringProvider` echoes keys — VM tests assert key names).

## Global Constraints

- No user-visible text hard-coded in XAML or C# — everything via `Strings.axaml` keys.
- Strict red/green TDD for all new non-trivial logic; observe the failing test before implementing.
- Every interactive control gets `ToolTip.Tip`; icon-only controls also `AutomationProperties.Name`.
- Every caught exception logs via `AppLog.Error/Warn` (except `OperationCanceledException`, swallowed silently). No bare `catch { }`.
- Every new `<Window>` carries `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`.
- `CHANGELOG.md` is frozen — do not touch it.
- Static services leak across serially-run tests — every static registration in a test needs a reset (constructor or `Dispose`), following `ChatterPrefixService.Clear()` / `SpeakerNameService.Register(empty)` precedents.
- Embed design reasoning as comments in the .cs/.axaml files, not only in the spec.

**Known spec deviations (agreed rationale, do not "fix" back):**
1. The spec says the index scan uses "streaming regex". Regex cannot reliably pair an `ExternalVO` value with its owning `NodeID` (property order inside the node object is not guaranteed), so the index uses `JsonDocument` per file instead — still no full model parse, still fast.
2. The spec says the picker lists "game + override" conversations "same source as the main browser". `Poe2GameDataProvider.EnumerateConversations()` (the browser's source) has **no** override support today; adding it is a provider-wide feature. The picker uses the provider list as-is (base game + project's new conversations); the **index** still scans override folders so shared-counts include mod usages.

---

### Task 1: `VoAliasParse` (pure, Core)

**Files:**
- Create: `DialogEditor.Core/Audio/VoAliasParse.cs`
- Test: `DialogEditor.Tests/Audio/VoAliasParseTests.cs` (**create**, new folder mirrors Core)

**Interfaces:**
- Produces (later tasks rely on these exact names):
  - `record VoAliasTarget(string SpeakerFolder, string Conversation, int NodeId)`
  - `static VoAliasTarget? VoAliasParse.TryParse(string? aliasPath)`

- [x] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/Audio/VoAliasParseTests.cs`:

```csharp
using DialogEditor.Core.Audio;

namespace DialogEditor.Tests.Audio;

// Shapes taken from the 2026-07-03 audit of all 787 distinct shipped values.
public class VoAliasParseTests
{
    [Fact]
    public void TryParse_CanonicalShape_SplitsFolderConversationAndId()
    {
        var t = VoAliasParse.TryParse("narrator/05_cv_dragon_dais_0001");
        Assert.NotNull(t);
        Assert.Equal("narrator", t!.SpeakerFolder);
        Assert.Equal("05_cv_dragon_dais", t.Conversation);
        Assert.Equal(1, t.NodeId);
    }

    [Fact]
    public void TryParse_HyphenatedConversation_Parses()
    {
        var t = VoAliasParse.TryParse("mt_magran/re_si_post_magransteeth_gods-2_0336");
        Assert.NotNull(t);
        Assert.Equal("re_si_post_magransteeth_gods-2", t!.Conversation);
        Assert.Equal(336, t.NodeId);
    }

    [Fact]
    public void TryParse_FolderWithSpaces_Parses()
    {
        var t = VoAliasParse.TryParse("erol of levi/27_cv_court_of_woedica_player_interrupts_0176");
        Assert.NotNull(t);
        Assert.Equal("erol of levi", t!.SpeakerFolder);
        Assert.Equal(176, t.NodeId);
    }

    [Fact]
    public void TryParse_UppercaseFilename_ParsesPreservingCase()
    {
        var t = VoAliasParse.TryParse("dawnstar_guide/sh_Dawnstar_Guide_09_cv_maren_0005");
        Assert.NotNull(t);
        Assert.Equal("sh_Dawnstar_Guide_09_cv_maren", t!.Conversation);
        Assert.Equal(5, t.NodeId);
    }

    [Fact]
    public void TryParse_FiveDigitId_Parses()
    {
        // The writer pads with {nodeId:0000} — a MINIMUM of four digits.
        var t = VoAliasParse.TryParse("eder/companion_eder_10234");
        Assert.NotNull(t);
        Assert.Equal(10234, t!.NodeId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no_slash_0001")]                  // no folder segment
    [InlineData("narrator/no_digit_suffix")]       // no trailing _#### block
    [InlineData("narrator/short_012")]             // fewer than four digits
    [InlineData("a/b/c_0001")]                     // two slashes — not a shipped shape
    [InlineData("narrator/")]                      // empty file segment
    [InlineData("/conv_0001")]                     // empty folder segment
    public void TryParse_NonMatching_ReturnsNull(string? input)
        => Assert.Null(VoAliasParse.TryParse(input));
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~VoAliasParseTests"`
Expected: **build failure** — `error CS0246: The type or namespace name 'DialogEditor.Core.Audio' could not be found`.

- [x] **Step 3: Implement**

Create `DialogEditor.Core/Audio/VoAliasParse.cs`:

```csharp
namespace DialogEditor.Core.Audio;

/// <summary>
/// The target a PoE2 <c>ExternalVO</c> alias points at, decomposed from
/// "&lt;speakerFolder&gt;/&lt;conversation&gt;_&lt;nodeId&gt;".
/// </summary>
public record VoAliasTarget(string SpeakerFolder, string Conversation, int NodeId);

/// <summary>
/// Parses PoE2 <c>ExternalVO</c> alias paths. The writer emits
/// "&lt;folder&gt;/&lt;conversation&gt;_{nodeId:0000}" — nodeId padded to a MINIMUM
/// of four digits (ids ≥ 10000 produce five). 782 of the 787 shipped values match
/// this shape; the rest (and hand-crafted values) return null and the UI falls
/// back to showing the raw path.
/// </summary>
public static class VoAliasParse
{
    public static VoAliasTarget? TryParse(string? aliasPath)
    {
        if (string.IsNullOrWhiteSpace(aliasPath)) return null;

        // Exactly one separator: folder / file. Shipped data never nests deeper.
        var parts = aliasPath.Split('/', '\\');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return null;

        var file = parts[1];
        var us   = file.LastIndexOf('_');
        if (us <= 0 || us == file.Length - 1) return null;

        var digits = file[(us + 1)..];
        if (digits.Length < 4 || !digits.All(char.IsAsciiDigit)) return null;
        if (!int.TryParse(digits, out var id)) return null;

        return new VoAliasTarget(parts[0], file[..us], id);
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~VoAliasParseTests"`
Expected: PASS (14 tests).

- [x] **Step 5: Commit**

```bash
git add DialogEditor.Core/Audio/VoAliasParse.cs DialogEditor.Tests/Audio/VoAliasParseTests.cs
git commit -m "feat(vo): VoAliasParse — decompose ExternalVO alias paths

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: `VoAliasIndexService` + game-open wiring

**Files:**
- Create: `DialogEditor.ViewModels/Services/VoAliasIndexService.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (after the `ChatterPrefixService.Register(provider.LoadChatterPrefixes());` line, currently ~1110)
- Test: `DialogEditor.Tests/Services/VoAliasIndexServiceTests.cs` (**create**)

**Interfaces:**
- Produces:
  - `record VoAliasRef(string Conversation, int NodeId)` — `Conversation` is the file name without extension, lower-cased.
  - `static class VoAliasIndexService` with:
    - `void Rebuild(string gameRoot)` (synchronous; caller backgrounds it)
    - `IReadOnlyList<VoAliasRef> GetReferences(string aliasPath)` (case-insensitive key; empty list when absent or not ready)
    - `bool IsReady { get; }`
    - `void RegisterForTests(IReadOnlyDictionary<string, IReadOnlyList<VoAliasRef>> refs)`
    - `void Clear()`

- [x] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/Services/VoAliasIndexServiceTests.cs`. Fixture pattern copied from `VoPathResolverTests` (temp game root, cleanup in `Dispose`):

```csharp
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class VoAliasIndexServiceTests : IDisposable
{
    private readonly string _gameRoot;
    private readonly string _baseConvDir;

    public VoAliasIndexServiceTests()
    {
        _gameRoot    = Path.Combine(Path.GetTempPath(), $"VoIdx_{Guid.NewGuid():N}");
        _baseConvDir = Path.Combine(_gameRoot,
            "PillarsOfEternityII_Data", "exported", "design", "conversations", "21_prologue");
        Directory.CreateDirectory(_baseConvDir);
        VoAliasIndexService.Clear();
    }

    public void Dispose()
    {
        VoAliasIndexService.Clear();
        if (Directory.Exists(_gameRoot))
            Directory.Delete(_gameRoot, recursive: true);
    }

    private static string ConvJson(params (int id, string? alias)[] nodes)
    {
        var items = nodes.Select(n =>
            $$"""{ "NodeID": {{n.id}}, "ExternalVO": "{{n.alias ?? ""}}" }""");
        return $$"""{ "Nodes": [ {{string.Join(",", items)}} ] }""";
    }

    [Fact]
    public void Rebuild_IndexesNonEmptyAliases_SkipsEmpty()
    {
        File.WriteAllText(Path.Combine(_baseConvDir, "21_si_test.conversationbundle"),
            ConvJson((1, "narrator/other_conv_0005"), (2, null), (3, "narrator/other_conv_0005")));

        VoAliasIndexService.Rebuild(_gameRoot);

        Assert.True(VoAliasIndexService.IsReady);
        var refs = VoAliasIndexService.GetReferences("narrator/other_conv_0005");
        Assert.Equal(2, refs.Count);
        Assert.Contains(new VoAliasRef("21_si_test", 1), refs);
        Assert.Contains(new VoAliasRef("21_si_test", 3), refs);
        Assert.Empty(VoAliasIndexService.GetReferences("narrator/nothing_0001"));
    }

    [Fact]
    public void Rebuild_KeysAreCaseInsensitive()
    {
        File.WriteAllText(Path.Combine(_baseConvDir, "21_a.conversationbundle"),
            ConvJson((1, "dawnstar_guide/sh_Dawnstar_Guide_0005")));

        VoAliasIndexService.Rebuild(_gameRoot);

        Assert.Single(VoAliasIndexService.GetReferences("DAWNSTAR_GUIDE/SH_DAWNSTAR_GUIDE_0005"));
    }

    [Fact]
    public void Rebuild_OverrideReplacesBaseContribution()
    {
        // Base version of 21_a aliases X twice; the override version only once.
        // Override wins per conversation — X must have exactly one reference.
        File.WriteAllText(Path.Combine(_baseConvDir, "21_a.conversationbundle"),
            ConvJson((1, "narrator/x_0001"), (2, "narrator/x_0001")));
        var overrideDir = Path.Combine(_gameRoot, "override", "SomeMod",
            "design", "conversations", "21_prologue");
        Directory.CreateDirectory(overrideDir);
        File.WriteAllText(Path.Combine(overrideDir, "21_a.conversationbundle"),
            ConvJson((7, "narrator/x_0001")));

        VoAliasIndexService.Rebuild(_gameRoot);

        var refs = VoAliasIndexService.GetReferences("narrator/x_0001");
        Assert.Equal([new VoAliasRef("21_a", 7)], refs);
    }

    [Fact]
    public void Rebuild_MalformedFile_IsSkipped_OthersStillIndexed()
    {
        File.WriteAllText(Path.Combine(_baseConvDir, "21_broken.conversationbundle"),
            "{ not valid json ");
        File.WriteAllText(Path.Combine(_baseConvDir, "21_ok.conversationbundle"),
            ConvJson((4, "narrator/y_0002")));

        VoAliasIndexService.Rebuild(_gameRoot);   // must not throw

        Assert.Single(VoAliasIndexService.GetReferences("narrator/y_0002"));
    }

    [Fact]
    public void GetReferences_BeforeRebuild_NotReadyAndEmpty()
    {
        Assert.False(VoAliasIndexService.IsReady);
        Assert.Empty(VoAliasIndexService.GetReferences("narrator/x_0001"));
    }

    [Fact]
    public void RegisterForTests_SetsReadyAndServesEntries()
    {
        VoAliasIndexService.RegisterForTests(new Dictionary<string, IReadOnlyList<VoAliasRef>>
        {
            ["narrator/z_0009"] = [new VoAliasRef("some_conv", 9)]
        });
        Assert.True(VoAliasIndexService.IsReady);
        Assert.Single(VoAliasIndexService.GetReferences("narrator/z_0009"));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~VoAliasIndexServiceTests"`
Expected: **build failure** — `'VoAliasIndexService' could not be found`.

- [x] **Step 3: Implement the service**

Create `DialogEditor.ViewModels/Services/VoAliasIndexService.cs` (copy the `using` set of `VoOrphanScanner.cs` so `AppLog` resolves, plus `System.Text.Json`):

```csharp
using System.Text.Json;

namespace DialogEditor.ViewModels.Services;

/// <summary>One node that references a VO alias path. Conversation is the
/// file name without extension, lower-cased.</summary>
public record VoAliasRef(string Conversation, int NodeId);

/// <summary>
/// Session-wide reverse index: ExternalVO alias path → nodes referencing it.
/// Same lifecycle as SpeakerNameService: rebuilt per game-root open, in memory
/// only, so every app start re-reads current disk state (including newly
/// installed mods). Scans the base game AND override/*/design/conversations,
/// override winning per conversation file name — matching the game's own
/// precedence. Uses JsonDocument (not the full conversation parser): we only
/// need NodeID/ExternalVO pairs, and property order inside a node object is
/// not guaranteed, which rules out a flat regex scan.
/// </summary>
public static class VoAliasIndexService
{
    private static Dictionary<string, List<VoAliasRef>> _refs = new(StringComparer.OrdinalIgnoreCase);
    public static bool IsReady { get; private set; }

    public static void Rebuild(string gameRoot)
    {
        // Conversation file name (no extension, lower) → winning full path.
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void AddTree(string root)
        {
            if (!Directory.Exists(root)) return;
            foreach (var f in Directory.EnumerateFiles(root, "*.conversation*", SearchOption.AllDirectories))
                files[Path.GetFileNameWithoutExtension(f).ToLowerInvariant()] = f; // later wins
        }

        AddTree(Path.Combine(gameRoot, "PillarsOfEternityII_Data", "exported", "design", "conversations"));
        var overrideRoot = Path.Combine(gameRoot, "override");
        if (Directory.Exists(overrideRoot))
            foreach (var mod in Directory.EnumerateDirectories(overrideRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                AddTree(Path.Combine(mod, "design", "conversations"));

        var map = new Dictionary<string, List<VoAliasRef>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (convName, path) in files)
            ScanFile(map, convName, path);

        _refs   = map;
        IsReady = true;
    }

    private static void ScanFile(Dictionary<string, List<VoAliasRef>> map, string convName, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("Nodes", out var nodes)
                || nodes.ValueKind != JsonValueKind.Array)
                return;

            foreach (var node in nodes.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object) continue;
                if (!node.TryGetProperty("ExternalVO", out var ext)
                    || ext.ValueKind != JsonValueKind.String) continue;
                var alias = ext.GetString();
                if (string.IsNullOrEmpty(alias)) continue;
                if (!node.TryGetProperty("NodeID", out var idEl)
                    || !idEl.TryGetInt32(out var id)) continue;

                if (!map.TryGetValue(alias, out var list))
                    map[alias] = list = [];
                list.Add(new VoAliasRef(convName, id));
            }
        }
        catch (Exception ex)
        {
            // Unreadable/malformed conversation: skip rather than fail the index.
            AppLog.Warn($"VO alias index: could not scan '{path}': {ex.Message}");
        }
    }

    public static IReadOnlyList<VoAliasRef> GetReferences(string aliasPath)
        => _refs.TryGetValue(aliasPath, out var list) ? list : [];

    /// Test seam — mirrors SpeakerNameService.Register.
    public static void RegisterForTests(IReadOnlyDictionary<string, IReadOnlyList<VoAliasRef>> refs)
    {
        _refs   = refs.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.OrdinalIgnoreCase);
        IsReady = true;
    }

    public static void Clear()
    {
        _refs   = new Dictionary<string, List<VoAliasRef>>(StringComparer.OrdinalIgnoreCase);
        IsReady = false;
    }
}
```

Note: if `AppLog` is not in the `DialogEditor.ViewModels.Services` namespace, add the same `using` line `VoOrphanScanner.cs` has for it.

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~VoAliasIndexServiceTests"`
Expected: PASS (6 tests).

- [x] **Step 5: Wire the rebuild into game-folder open**

In `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`, directly after the line
`ChatterPrefixService.Register(provider.LoadChatterPrefixes());` (~1110), add:

```csharp
            // Reverse alias index for the detail pane's "also used by N nodes"
            // line. Disk-only and PoE2-only; a few seconds in the background.
            VoAliasIndexService.Clear();
            if (string.Equals(provider.GameId, "poe2", StringComparison.OrdinalIgnoreCase))
            {
                var aliasScanRoot = path;
                _ = Task.Run(() =>
                {
                    try { VoAliasIndexService.Rebuild(aliasScanRoot); }
                    catch (Exception ex) { AppLog.Error($"VO alias index rebuild failed: {ex.Message}"); }
                });
            }
```

(`path` is the game-root local already in scope there — the same value passed to `SpeakerNameService`-adjacent calls; verify the local's name at the insertion point and use it.)

- [x] **Step 6: Full build + test, commit**

```
dotnet build && dotnet test --nologo
```
Expected: build success, all tests pass.

```bash
git add DialogEditor.ViewModels/Services/VoAliasIndexService.cs DialogEditor.Tests/Services/VoAliasIndexServiceTests.cs DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git commit -m "feat(vo): session alias index with override-wins scan

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: `NodeDetailViewModel` alias surface

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/NodeDetailViewModelPaneTests.cs` (extend)

**Interfaces:**
- Consumes: Task 1 `VoAliasParse.TryParse`, Task 2 `VoAliasIndexService`/`VoAliasRef`.
- Produces (XAML and Tasks 6–7 rely on these exact names):
  - `record VoAliasUse(string Conversation, int NodeId, string AliasPath)` (in `NodeDetailViewModel.cs`, below the class or in the same namespace file scope)
  - `bool HasVoAlias`, `string VoAliasRawPath`, `string VoAliasDescription`
  - `int? VoAliasSharedCount` (null until index ready), `string VoAliasSharedText`
  - `bool CanStartVoAliasPick` (PoE2 + node loaded + no alias yet)
  - `Func<IReadOnlyList<VoAliasUse>>? ProjectAliasOverlay { get; set; }`
  - `Func<string?, Task<string?>>? ShowAliasPicker { get; set; }`
  - `IRelayCommand PickVoAliasCommand`, `IRelayCommand ClearVoAliasCommand` (generated from `[RelayCommand]` methods `PickVoAlias`/`ClearVoAlias`)

- [x] **Step 1: Write the failing tests**

Append to `DialogEditor.Tests/ViewModels/NodeDetailViewModelPaneTests.cs` (inside the class). Also add `VoAliasIndexService.Clear();` to the constructor (below the `SpeakerNameService.Register` line) and note the class needs no `IDisposable` — each test that registers index data must clear it in a `finally`.

The `LoadNode` helper creates nodes with `ExternalVO: ""`; add an optional parameter `string externalVO = ""` to it and pass it through to the `ConversationNode` constructor (`ExternalVO: externalVO`). For the alias tests to have a non-null `_voCheck`, PoE2 context is required: set `_vm.ActiveGameId = "poe2";` and `_vm.GameRoot = <temp dir>;` — follow how `NodeDetailViewModelPlaybackTests.cs` arranges PoE2 context (check that file first; it exercises `_voCheck`-dependent behavior and shows the exact property names/setters available).

```csharp
    // ── ExternalVO alias surface (2026-07-03 alias UX) ───────────────────

    [Fact]
    public void HasVoAlias_FalseWithoutAlias_TrueWithAlias()
    {
        LoadPoe2Node(externalVO: "");
        Assert.False(_vm.HasVoAlias);
        LoadPoe2Node(externalVO: "narrator/other_conv_0005");
        Assert.True(_vm.HasVoAlias);
    }

    [Fact]
    public void VoAliasDescription_ParseableAlias_UsesFriendlyKey()
    {
        LoadPoe2Node(externalVO: "narrator/other_conv_0005");
        // StubStringProvider echoes the Loc.Format key.
        Assert.StartsWith("NodeDetail_AliasDescription", _vm.VoAliasDescription);
    }

    [Fact]
    public void VoAliasDescription_UnparseableAlias_FallsBackToRawKey()
    {
        LoadPoe2Node(externalVO: "narrator/no_digits_here");
        Assert.StartsWith("NodeDetail_AliasRaw", _vm.VoAliasDescription);
    }

    [Fact]
    public void VoAliasSharedCount_NullBeforeIndexReady()
    {
        LoadPoe2Node(externalVO: "narrator/other_conv_0005");
        Assert.Null(_vm.VoAliasSharedCount);
        Assert.Equal(string.Empty, _vm.VoAliasSharedText);
    }

    [Fact]
    public void VoAliasSharedCount_CountsOthers_ExcludingSelf()
    {
        try
        {
            VoAliasIndexService.RegisterForTests(new Dictionary<string, IReadOnlyList<VoAliasRef>>
            {
                ["narrator/other_conv_0005"] =
                [
                    new VoAliasRef("conv_a", 3),
                    new VoAliasRef("conv_b", 9),
                ]
            });
            LoadPoe2Node(externalVO: "narrator/other_conv_0005");
            Assert.Equal(2, _vm.VoAliasSharedCount);
            Assert.StartsWith("NodeDetail_AliasSharedCount", _vm.VoAliasSharedText);
        }
        finally { VoAliasIndexService.Clear(); }
    }

    [Fact]
    public void VoAliasSharedCount_OverlayShadowsDiskEntries()
    {
        try
        {
            VoAliasIndexService.RegisterForTests(new Dictionary<string, IReadOnlyList<VoAliasRef>>
            {
                ["narrator/other_conv_0005"] = [new VoAliasRef("conv_a", 3)]
            });
            // In-memory state says conv_a node 3 no longer aliases this path,
            // but conv_c node 4 now does.
            _vm.ProjectAliasOverlay = () =>
            [
                new VoAliasUse("conv_a", 3, ""),
                new VoAliasUse("conv_c", 4, "narrator/other_conv_0005"),
            ];
            LoadPoe2Node(externalVO: "narrator/other_conv_0005");
            Assert.Equal(1, _vm.VoAliasSharedCount);
        }
        finally { VoAliasIndexService.Clear(); }
    }

    [Fact]
    public void ClearVoAlias_EmptiesExternalVO()
    {
        LoadPoe2Node(externalVO: "narrator/other_conv_0005");
        _vm.ClearVoAliasCommand.Execute(null);
        Assert.False(_vm.HasVoAlias);
        Assert.Equal(string.Empty, _vm.ExternalVO);
    }

    [Fact]
    public async Task PickVoAlias_WritesPickerResult()
    {
        LoadPoe2Node(externalVO: "");
        _vm.ShowAliasPicker = _ => Task.FromResult<string?>("eder/some_conv_0042");
        await _vm.PickVoAliasCommand.ExecuteAsync(null);
        Assert.Equal("eder/some_conv_0042", _vm.ExternalVO);
        Assert.True(_vm.HasVoAlias);
    }

    [Fact]
    public async Task PickVoAlias_NullResult_LeavesAliasUnchanged()
    {
        LoadPoe2Node(externalVO: "narrator/keep_me_0001");
        _vm.ShowAliasPicker = _ => Task.FromResult<string?>(null);
        await _vm.PickVoAliasCommand.ExecuteAsync(null);
        Assert.Equal("narrator/keep_me_0001", _vm.ExternalVO);
    }

    [Fact]
    public void HasVoAlias_Poe1_AlwaysFalse()
    {
        // No PoE2 context (default test state): _voCheck is null → alias UI hidden
        // even if the data somehow carried a value.
        LoadNode();
        Assert.False(_vm.HasVoAlias);
        Assert.False(_vm.CanStartVoAliasPick);
    }
```

Add a `LoadPoe2Node(string externalVO)` helper next to `LoadNode` that sets PoE2 context (game id + a temp game root directory) before calling `LoadNode(externalVO: externalVO)` — mirror the arrangement in `NodeDetailViewModelPlaybackTests.cs`, including any cleanup it does.

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~NodeDetailViewModelPaneTests"`
Expected: **build failure** — `'NodeDetailViewModel' does not contain a definition for 'HasVoAlias'`.

- [x] **Step 3: Implement**

In `NodeDetailViewModel.cs`, add `using DialogEditor.Core.Audio;` and, after the expander-state block from the pane rework, add:

```csharp
    // ── ExternalVO alias surface (2026-07-03 alias UX) ───────────────────
    // ExternalVO redirects this node's VO to ANOTHER line's recording — shipped
    // PoE2 data aliases 1,000 nodes this way, often across conversation files.
    // The pane therefore explains the alias instead of exposing a raw textbox;
    // edits go through the node picker so the value is always derivable.

    /// PoE2-gated: _voCheck is null on PoE1/no game root, hiding the alias UI there.
    public bool HasVoAlias =>
        _voCheck is not null && !string.IsNullOrEmpty(_node?.ExternalVO);

    public string VoAliasRawPath => _node?.ExternalVO ?? string.Empty;

    /// "Plays the recording of <conv> node <id>" — or the raw path when unparseable.
    public string VoAliasDescription
    {
        get
        {
            if (!HasVoAlias) return string.Empty;
            var t = VoAliasParse.TryParse(_node!.ExternalVO);
            return t is null
                ? Loc.Format("NodeDetail_AliasRaw", _node.ExternalVO)
                : Loc.Format("NodeDetail_AliasDescription", t.Conversation, t.NodeId);
        }
    }

    /// Set by MainWindowViewModel — current in-memory (conversation, nodeId, alias)
    /// triples from the open project; these shadow the disk index so mid-session
    /// edits are reflected before F5 writes them to the game folder.
    public Func<IReadOnlyList<VoAliasUse>>? ProjectAliasOverlay { get; set; }

    /// Other nodes sharing this alias (self excluded); null while the background
    /// index scan has not finished.
    public int? VoAliasSharedCount
    {
        get
        {
            if (!HasVoAlias || !VoAliasIndexService.IsReady || _node is null) return null;
            var alias   = _node.ExternalVO;
            var selfConv = (Canvas?.ConversationName ?? string.Empty).ToLowerInvariant();
            var overlay = ProjectAliasOverlay?.Invoke() ?? [];
            var shadowed = overlay
                .Select(u => (Conv: u.Conversation.ToLowerInvariant(), u.NodeId))
                .ToHashSet();

            var effective = VoAliasIndexService.GetReferences(alias)
                .Select(r => (Conv: r.Conversation.ToLowerInvariant(), r.NodeId))
                .Where(r => !shadowed.Contains(r))
                .Concat(overlay
                    .Where(u => string.Equals(u.AliasPath, alias, StringComparison.OrdinalIgnoreCase))
                    .Select(u => (Conv: u.Conversation.ToLowerInvariant(), u.NodeId)))
                .Distinct()
                .Count(r => !(r.Conv == selfConv && r.NodeId == _node.NodeId));
            return effective;
        }
    }

    public string VoAliasSharedText => VoAliasSharedCount switch
    {
        null => string.Empty,
        0    => Loc.Get("NodeDetail_AliasNotShared"),
        var n => Loc.Format("NodeDetail_AliasSharedCount", n),
    };

    /// "Reuse another line's VO…" visibility: PoE2 node loaded, no alias yet.
    public bool CanStartVoAliasPick => _voCheck is not null && !HasVoAlias;

    /// Set by MainWindow.axaml.cs — opens the picker (current alias in, chosen
    /// alias out, null = cancelled).
    public Func<string?, Task<string?>>? ShowAliasPicker { get; set; }

    [RelayCommand]
    private async Task PickVoAlias()
    {
        if (_node is null || ShowAliasPicker is null) return;
        var result = await ShowAliasPicker(HasVoAlias ? _node.ExternalVO : null);
        if (result is not null)
            _node.ExternalVO = result;   // undoable via NodeViewModel.Push
    }

    [RelayCommand]
    private void ClearVoAlias()
    {
        if (_node is not null)
            _node.ExternalVO = string.Empty;   // undoable via NodeViewModel.Push
    }
```

Add `record VoAliasUse(string Conversation, int NodeId, string AliasPath);` at the bottom of the same file (file-scope, outside the class), with a doc comment.

At the end of `NotifyAllProxies()` (after the summary notifications) add:

```csharp
        OnPropertyChanged(nameof(HasVoAlias));
        OnPropertyChanged(nameof(VoAliasRawPath));
        OnPropertyChanged(nameof(VoAliasDescription));
        OnPropertyChanged(nameof(VoAliasSharedCount));
        OnPropertyChanged(nameof(VoAliasSharedText));
        OnPropertyChanged(nameof(CanStartVoAliasPick));
```

- [x] **Step 4: Run tests, fix, commit**

Run: `dotnet test --nologo --filter "FullyQualifiedName~NodeDetailViewModelPaneTests"` → PASS, then `dotnet test --nologo` → all pass.

```bash
git add DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs DialogEditor.Tests/ViewModels/NodeDetailViewModelPaneTests.cs
git commit -m "feat(detail-pane): ExternalVO alias properties, picker/clear commands

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Project alias overlay wiring (MainWindowViewModel)

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelAliasOverlayTests.cs` (**create**) — only if `MainWindowViewModel` is constructible in tests (check how `MainWindowViewModelApplyTests.cs` constructs it and follow that pattern); if it requires heavy scaffolding, put the extraction logic in a static helper and test that instead (preferred, shown below).

**Interfaces:**
- Consumes: Task 3 `VoAliasUse`, `Detail.ProjectAliasOverlay`; `DialogProject.Patches` (`ConversationPatch.AddedNodes: NodeEditSnapshot` with `.ExternalVO`, `.ModifiedNodes: NodeModification` with `FieldChanges["ExternalVO"].To` JSON-encoded), `ConversationViewModel.Nodes` (`NodeViewModel.ExternalVO`).
- Produces: `static class VoAliasOverlayBuilder` with
  `IReadOnlyList<VoAliasUse> Build(IReadOnlyDictionary<string, ConversationPatch>? patches, string? openConversation, IEnumerable<(int NodeId, string ExternalVO)>? openNodes)`

- [x] **Step 1: Write the failing test**

Create `DialogEditor.Tests/Services/VoAliasOverlayBuilderTests.cs`:

```csharp
using System.Text.Json;
using DialogEditor.Patch;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class VoAliasOverlayBuilderTests
{
    private static ConversationPatch PatchWith(
        string conv,
        (int id, string alias)[] added,
        (int id, string alias)[] modified)
        => new(
            ConversationName: conv,
            SchemaVersion: ConversationPatch.CurrentSchemaVersion,
            AddedNodes: added.Select(a => new DialogEditor.Core.Editing.NodeEditSnapshot(
                NodeId: a.id, IsPlayerChoice: false,
                SpeakerCategory: DialogEditor.Core.Models.SpeakerCategory.Npc,
                SpeakerGuid: "", ListenerGuid: "",
                DefaultText: "", FemaleText: "",
                DisplayType: "Conversation", Persistence: "None",
                ActorDirection: "", Comments: "",
                ExternalVO: a.alias, HasVO: false, HideSpeaker: false,
                Links: [], Conditions: [], Scripts: [])).ToList(),
            DeletedNodeIds: [],
            ModifiedNodes: modified.Select(m => new NodeModification(
                m.id,
                new Dictionary<string, FieldChange>
                {
                    ["ExternalVO"] = new FieldChange(
                        JsonSerializer.Serialize(""), JsonSerializer.Serialize(m.alias))
                },
                [], [])).ToList());

    [Fact]
    public void Build_CollectsAddedAndModifiedNodes()
    {
        var patches = new Dictionary<string, ConversationPatch>
        {
            ["conv_a"] = PatchWith("conv_a",
                added: [(100, "narrator/x_0001")],
                modified: [(3, "narrator/y_0002")]),
        };

        var uses = VoAliasOverlayBuilder.Build(patches, null, null);

        Assert.Contains(new VoAliasUse("conv_a", 100, "narrator/x_0001"), uses);
        Assert.Contains(new VoAliasUse("conv_a", 3, "narrator/y_0002"), uses);
    }

    [Fact]
    public void Build_OpenCanvasNodes_WinOverPatchForSameConversation()
    {
        var patches = new Dictionary<string, ConversationPatch>
        {
            ["conv_a"] = PatchWith("conv_a", added: [], modified: [(3, "narrator/old_0001")]),
        };

        var uses = VoAliasOverlayBuilder.Build(
            patches, "conv_a", [(3, "narrator/new_0002"), (4, "")]);

        Assert.Contains(new VoAliasUse("conv_a", 3, "narrator/new_0002"), uses);
        Assert.DoesNotContain(uses, u => u.AliasPath == "narrator/old_0001");
        // Empty alias still shadows the disk entry (means "no longer aliased").
        Assert.Contains(new VoAliasUse("conv_a", 4, ""), uses);
    }

    [Fact]
    public void Build_NullInputs_ReturnsEmpty()
        => Assert.Empty(VoAliasOverlayBuilder.Build(null, null, null));
}
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test --nologo --filter "FullyQualifiedName~VoAliasOverlayBuilderTests"`
Expected: **build failure** — `'VoAliasOverlayBuilder' could not be found`.
(If the `NodeEditSnapshot`/`NodeModification` constructions themselves fail to compile, adjust the test's construction to the real signatures — they are `DialogEditor.Core.Editing.NodeEditSnapshot` (17 positional params as of this writing) and `DialogEditor.Patch.NodeModification`'s 4-arg convenience constructor.)

- [x] **Step 3: Implement**

Create `DialogEditor.ViewModels/Services/VoAliasOverlayBuilder.cs`:

```csharp
using System.Text.Json;
using DialogEditor.Patch;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Builds the in-memory ExternalVO overlay for the alias shared-count: the open
/// project's patched values (added + modified nodes) with the live canvas nodes
/// winning for the open conversation. An entry with an empty AliasPath still
/// matters — it shadows a disk-index reference that the session has removed.
/// </summary>
public static class VoAliasOverlayBuilder
{
    public static IReadOnlyList<VoAliasUse> Build(
        IReadOnlyDictionary<string, ConversationPatch>? patches,
        string? openConversation,
        IEnumerable<(int NodeId, string ExternalVO)>? openNodes)
    {
        var uses = new Dictionary<(string Conv, int Id), string>();

        if (patches is not null)
            foreach (var (conv, patch) in patches)
            {
                foreach (var added in patch.AddedNodes)
                    uses[(conv, added.NodeId)] = added.ExternalVO;
                foreach (var mod in patch.ModifiedNodes)
                    if (mod.FieldChanges.TryGetValue("ExternalVO", out var fc))
                        uses[(conv, mod.NodeId)] =
                            JsonSerializer.Deserialize<string>(fc.To) ?? string.Empty;
            }

        if (openConversation is not null && openNodes is not null)
            foreach (var (id, ext) in openNodes)
                uses[(openConversation, id)] = ext;   // live canvas wins

        return uses.Select(kv => new VoAliasUse(kv.Key.Conv, kv.Key.Id, kv.Value)).ToList();
    }
}
```

- [x] **Step 4: Run test to verify it passes**

Run: `dotnet test --nologo --filter "FullyQualifiedName~VoAliasOverlayBuilderTests"` → PASS.

- [x] **Step 5: Wire into MainWindowViewModel**

In `MainWindowViewModel`, wherever `Detail` delegates are first assigned (search for `Detail.AttributionLookup =` or the constructor block assigning `Detail.` members), add:

```csharp
        Detail.ProjectAliasOverlay = () => VoAliasOverlayBuilder.Build(
            _project?.Patches,
            CurrentConversationName,
            Conversation?.Nodes.Select(n => (n.NodeId, n.ExternalVO)));
```

`CurrentConversationName` exists (seen at ~line 1083); the canvas property may be named `Conversation` or similar — find the `ConversationViewModel`-typed property on `MainWindowViewModel` (the one whose `ConversationName`/`Nodes` are used elsewhere, e.g. wherever `RefreshLinks` or `Nodes.Any(` is called from) and use its actual name. If `_project` is not the project field's name, match the field used in `MainWindowViewModelApplyTests`-covered code.

- [x] **Step 6: Full build + test, commit**

```
dotnet build && dotnet test --nologo
```

```bash
git add DialogEditor.ViewModels/Services/VoAliasOverlayBuilder.cs DialogEditor.Tests/Services/VoAliasOverlayBuilderTests.cs DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git commit -m "feat(vo): project alias overlay feeds detail-pane shared count

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Strings + Voice group XAML rework

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (pane-rework block)
- Modify: `DialogEditor.Avalonia/Views/NodeDetailView.axaml` (Voice expander content only)

**Interfaces:**
- Consumes: Task 3 properties/commands (exact names above).

- [x] **Step 1: Add the strings**

Append inside the `<!-- ── Node detail pane rework (2026-07-02) ── -->` block of `Strings.axaml`:

```xml
    <!-- ── ExternalVO alias UX (2026-07-03) ─────────────────────────────── -->
    <!-- {0} = conversation name, {1} = node id -->
    <sys:String x:Key="NodeDetail_AliasDescription">Plays the recording of {0} node {1}</sys:String>
    <!-- {0} = raw alias path -->
    <sys:String x:Key="NodeDetail_AliasRaw">Plays external recording: {0}</sys:String>
    <sys:String x:Key="NodeDetail_AliasNotShared">Not shared with other nodes</sys:String>
    <!-- {0} = count of other nodes using the same recording -->
    <sys:String x:Key="NodeDetail_AliasSharedCount">Also used by {0} other node(s)</sys:String>
    <sys:String x:Key="NodeDetail_ReuseVoButton">Reuse another line's VO…</sys:String>
    <sys:String x:Key="NodeDetail_AliasChange">Change…</sys:String>
    <sys:String x:Key="NodeDetail_AliasClear">Clear</sys:String>
    <sys:String x:Key="ToolTip_ReuseVo">Point this node's voice-over at another line's recording instead of its own file. Opens a picker listing every conversation and node.</sys:String>
    <sys:String x:Key="ToolTip_AliasChange">Choose a different node whose recording this node should play.</sys:String>
    <sys:String x:Key="ToolTip_AliasClear">Remove the alias. The node goes back to its own voice-over file, named after this conversation and node ID.</sys:String>
    <sys:String x:Key="ToolTip_AliasRawPath">The stored ExternalVO path, relative to the game's Voices folder. Edit it via Change…; it cannot be typed directly.</sys:String>
    <sys:String x:Key="ToolTip_AliasShared">How many other nodes play this same recording. Overwriting the file affects all of them.</sys:String>
```

- [x] **Step 2: Rework the Voice expander content**

In `NodeDetailView.axaml`, inside the Voice `Expander`'s inner `StackPanel`, **delete** these two elements:

```xml
                <TextBlock Classes="field-label" Text="{DynamicResource PropertyRow_ExternalVO}"/>
                <TextBox Classes="detail-field"
                         Text="{Binding ExternalVO, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         FontFamily="Consolas,Courier New,monospace"
                         ToolTip.Tip="{DynamicResource ToolTip_ExternalVO}"
                         AutomationProperties.HelpText="{DynamicResource ToolTip_ExternalVO}"
                         AutomationProperties.Name="{DynamicResource PropertyRow_ExternalVO}"/>
```

and insert, directly after the `HasVO` CheckBox:

```xml
                <!-- ── ExternalVO alias (2026-07-03 alias UX) ──────────────
                     The alias redirects playback to ANOTHER line's recording;
                     shipped PoE2 data does this on 1,000 nodes. The raw path is
                     readonly — edits go through the node picker so the value is
                     always derivable and the shared-count below stays honest. -->
                <StackPanel IsVisible="{Binding HasVoAlias}">
                    <TextBlock Text="{Binding VoAliasDescription}"
                               Foreground="{DynamicResource Brush.Text.Secondary}"
                               FontSize="{DynamicResource FontSize.Small}"
                               TextWrapping="Wrap"
                               ToolTip.Tip="{Binding VoAliasDescription}"/>
                    <SelectableTextBlock Text="{Binding VoAliasRawPath}"
                               FontFamily="Consolas,Courier New,monospace"
                               Foreground="{DynamicResource Brush.Text.Muted}"
                               FontSize="{DynamicResource FontSize.Caption}"
                               ToolTip.Tip="{DynamicResource ToolTip_AliasRawPath}"
                               Margin="0,1,0,0"/>
                    <TextBlock Text="{Binding VoAliasSharedText}"
                               Foreground="{DynamicResource Brush.Text.Muted}"
                               FontSize="{DynamicResource FontSize.Caption}"
                               ToolTip.Tip="{DynamicResource ToolTip_AliasShared}"
                               Margin="0,1,0,2"/>
                    <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
                        <Button Content="{DynamicResource NodeDetail_AliasChange}"
                                Command="{Binding PickVoAliasCommand}"
                                Padding="6,2"
                                ToolTip.Tip="{DynamicResource ToolTip_AliasChange}"
                                AutomationProperties.HelpText="{DynamicResource ToolTip_AliasChange}"/>
                        <Button Content="{DynamicResource NodeDetail_AliasClear}"
                                Command="{Binding ClearVoAliasCommand}"
                                Padding="6,2" Margin="4,0,0,0"
                                ToolTip.Tip="{DynamicResource ToolTip_AliasClear}"
                                AutomationProperties.HelpText="{DynamicResource ToolTip_AliasClear}"/>
                    </StackPanel>
                </StackPanel>

                <!-- Entry point when no alias is set (PoE2 only — hidden on PoE1
                     where ExternalVO is empty on all 40,991 shipped nodes). -->
                <Button Content="{DynamicResource NodeDetail_ReuseVoButton}"
                        Command="{Binding PickVoAliasCommand}"
                        IsVisible="{Binding CanStartVoAliasPick}"
                        Padding="6,2" Margin="0,2,0,4"
                        ToolTip.Tip="{DynamicResource ToolTip_ReuseVo}"
                        AutomationProperties.HelpText="{DynamicResource ToolTip_ReuseVo}"/>
```

Note: `PropertyRow_ExternalVO`/`ToolTip_ExternalVO` string keys stay in `Strings.axaml` if referenced elsewhere — run
`grep -rn "PropertyRow_ExternalVO\|ToolTip_ExternalVO" DialogEditor.Avalonia DialogEditor.ViewModels` and delete the keys only if this was the sole reference.

- [x] **Step 3: Build, test, commit**

```
dotnet build && dotnet test --nologo
```
Expected: build success (no `AVLN` errors), all tests pass.

```bash
git add "DialogEditor.Avalonia/Views/NodeDetailView.axaml" "DialogEditor.Avalonia/Resources/Strings.axaml"
git commit -m "feat(detail-pane): alias block replaces External VO textbox

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: `VoAliasPickerViewModel`

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/VoAliasPickerViewModel.cs`
- Modify: `DialogEditor.ViewModels/Services/VoPathResolver.cs` (extract `VoicesRoot` helper)
- Test: `DialogEditor.Tests/ViewModels/VoAliasPickerViewModelTests.cs` (**create**)

**Interfaces:**
- Consumes: `IGameDataProvider.EnumerateConversations()/LoadConversation(file)/FindConversation(name)`, `Conversation(Name, Nodes, Strings)`, `StringTable.Get(int)`, `VoPathResolver.ExpectedRelativePath`, `SpeakerNameService.FindByGuid`, Task 1 `VoAliasParse`.
- Produces (window XAML binds these exact names):
  - `static string VoPathResolver.VoicesRoot(string gameRoot)`
  - `record VoAliasPickerRow(int NodeId, string SpeakerName, string TextPreview, string? DerivedAlias, bool WemExists)` with `bool IsPickable => DerivedAlias is not null;` and `string WemGlyph => WemExists ? "✓" : "✗";`
  - `class VoAliasPickerViewModel` with: `IReadOnlyList<ConversationFile> AllConversations`, `ObservableCollection<ConversationFile> VisibleConversations`, `string ConversationFilter`, `ConversationFile? SelectedConversation`, `ObservableCollection<VoAliasPickerRow> VisibleRows`, `string NodeFilter`, `VoAliasPickerRow? SelectedRow`, `string? ResultAlias`, ctor `(IGameDataProvider provider, string gameRoot, string? currentAlias)`.

- [x] **Step 1: Refactor helper under green**

In `VoPathResolver.cs`, both `Check` and `WithLocalVoFallback` build the same Voices path. Extract:

```csharp
    /// Canonical Voices root for a PoE2 game folder — single definition so the
    /// picker, importer, and resolver can never disagree on the layout.
    public static string VoicesRoot(string gameRoot) => Path.Combine(gameRoot,
        "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
```

Replace both inline `Path.Combine(gameRoot, "PillarsOfEternityII_Data", ...)` occurrences with `VoicesRoot(gameRoot)`. Run `dotnet test --nologo --filter "FullyQualifiedName~VoPathResolverTests"` → all pass (pure refactor).

- [x] **Step 2: Write the failing tests**

Create `DialogEditor.Tests/ViewModels/VoAliasPickerViewModelTests.cs`:

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class VoAliasPickerViewModelTests : IDisposable
{
    private readonly string _gameRoot;

    private sealed class FakeProvider : IGameDataProvider
    {
        public Dictionary<string, Conversation> Convs { get; } = [];
        public string GameName => "Fake"; public string GameId => "poe2";
        public IReadOnlyList<string> AvailableLanguages => ["en"];
        public string Language { get; set; } = "en";
        public IReadOnlyList<ConversationFile> EnumerateConversations()
            => Convs.Keys.Select(BuildNewConversationFile).ToList();
        public Conversation LoadConversation(ConversationFile file) => Convs[file.Name];
        public IReadOnlyDictionary<string, string> LoadSpeakerNames() => new Dictionary<string, string>();
        public void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot) { }
        public string GetStringTablePath(ConversationFile file) => "";
        public string GetStringTablePath(ConversationFile file, string language) => "";
        public (string, string) GetBackupRoots() => ("", "");
        public ConversationFile BuildNewConversationFile(string name)
            => new(name, name + ".conversationbundle", "");
        public void InitializeConversationFile(ConversationFile file) { }
    }

    private readonly FakeProvider _provider = new();

    public VoAliasPickerViewModelTests()
    {
        Loc.Configure(new StubStringProvider());
        _gameRoot = Path.Combine(Path.GetTempPath(), $"VoPick_{Guid.NewGuid():N}");
        Directory.CreateDirectory(VoPathResolver.VoicesRoot(_gameRoot));
        ChatterPrefixService.Register(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "eder" }
        });
        SpeakerNameService.Register(new Dictionary<string, string>());
    }

    public void Dispose()
    {
        ChatterPrefixService.Clear();
        if (Directory.Exists(_gameRoot)) Directory.Delete(_gameRoot, recursive: true);
    }

    private static ConversationNode Node(int id, string speakerGuid) => new(
        NodeId: id, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
        SpeakerGuid: speakerGuid, ListenerGuid: "",
        Links: [], Conditions: [], Scripts: [],
        DisplayType: "Conversation", Persistence: "None");

    private void AddConv(string name, params ConversationNode[] nodes)
        => _provider.Convs[name] = new Conversation(name, nodes,
            new StringTable(nodes.Select(n => new StringEntry(n.NodeId, $"line {n.NodeId}", ""))));

    [Fact]
    public void SelectingConversation_BuildsRows_WithDerivedAlias()
    {
        AddConv("some_conv", Node(42, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
        var vm = new VoAliasPickerViewModel(_provider, _gameRoot, currentAlias: null);

        vm.SelectedConversation = vm.AllConversations.Single(c => c.Name == "some_conv");

        var row = Assert.Single(vm.VisibleRows);
        Assert.Equal(42, row.NodeId);
        Assert.True(row.IsPickable);
        Assert.Equal(Path.Combine("eder", "some_conv_0042"), row.DerivedAlias);
        Assert.Equal("line 42", row.TextPreview);
        Assert.False(row.WemExists);
    }

    [Fact]
    public void UnknownSpeakerPrefix_RowNotPickable()
    {
        AddConv("some_conv", Node(1, "totally-unknown-guid"));
        var vm = new VoAliasPickerViewModel(_provider, _gameRoot, null);
        vm.SelectedConversation = vm.AllConversations.Single();

        var row = Assert.Single(vm.VisibleRows);
        Assert.False(row.IsPickable);
        Assert.Null(row.DerivedAlias);
    }

    [Fact]
    public void WemExists_ReflectsFileOnDisk()
    {
        AddConv("some_conv", Node(7, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
        var wemDir = Path.Combine(VoPathResolver.VoicesRoot(_gameRoot), "eder");
        Directory.CreateDirectory(wemDir);
        File.WriteAllText(Path.Combine(wemDir, "some_conv_0007.wem"), "");

        var vm = new VoAliasPickerViewModel(_provider, _gameRoot, null);
        vm.SelectedConversation = vm.AllConversations.Single();

        Assert.True(Assert.Single(vm.VisibleRows).WemExists);
    }

    [Fact]
    public void NodeFilter_NarrowsByTextOrId()
    {
        AddConv("some_conv",
            Node(1, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"),
            Node(2, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
        var vm = new VoAliasPickerViewModel(_provider, _gameRoot, null);
        vm.SelectedConversation = vm.AllConversations.Single();

        vm.NodeFilter = "line 2";
        Assert.Equal(2, Assert.Single(vm.VisibleRows).NodeId);
        vm.NodeFilter = "1";
        Assert.Equal(1, Assert.Single(vm.VisibleRows).NodeId);
        vm.NodeFilter = "";
        Assert.Equal(2, vm.VisibleRows.Count);
    }

    [Fact]
    public void ConversationFilter_NarrowsList()
    {
        AddConv("alpha_conv", Node(1, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
        AddConv("beta_conv",  Node(1, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
        var vm = new VoAliasPickerViewModel(_provider, _gameRoot, null);

        vm.ConversationFilter = "beta";
        Assert.Equal("beta_conv", Assert.Single(vm.VisibleConversations).Name);
    }

    [Fact]
    public void CurrentAlias_PreselectsConversationAndRow()
    {
        AddConv("some_conv",
            Node(1, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"),
            Node(9, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
        var vm = new VoAliasPickerViewModel(_provider, _gameRoot, "eder/some_conv_0009");

        Assert.Equal("some_conv", vm.SelectedConversation?.Name);
        Assert.Equal(9, vm.SelectedRow?.NodeId);
        Assert.Equal(Path.Combine("eder", "some_conv_0009"), vm.ResultAlias);
    }

    [Fact]
    public void ResultAlias_NullWithoutSelection()
    {
        AddConv("some_conv", Node(1, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
        var vm = new VoAliasPickerViewModel(_provider, _gameRoot, null);
        Assert.Null(vm.ResultAlias);
    }
}
```

Note: `ConversationFile`'s constructor may differ from `new(name, path, folder)` — check `DialogEditor.Core/GameData/ConversationFile.cs` and match its real shape in `BuildNewConversationFile`.

- [x] **Step 3: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~VoAliasPickerViewModelTests"`
Expected: **build failure** — `'VoAliasPickerViewModel' could not be found`.

- [x] **Step 4: Implement**

Create `DialogEditor.ViewModels/ViewModels/VoAliasPickerViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Audio;
using DialogEditor.Core.GameData;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// <summary>One selectable node in the VO alias picker.</summary>
public record VoAliasPickerRow(
    int NodeId, string SpeakerName, string TextPreview, string? DerivedAlias, bool WemExists)
{
    public bool   IsPickable => DerivedAlias is not null;
    public string WemGlyph   => WemExists ? "✓" : "✗";
}

/// <summary>
/// Backs VoAliasPickerWindow: choose a conversation, then a node whose recording
/// this node should reuse. The alias is DERIVED from the picked node's canonical
/// path (VoPathResolver.ExpectedRelativePath) — never typed — so it always
/// resolves under the game's Voices folder. Conversations are parsed one at a
/// time on selection; nothing is bulk-loaded.
/// </summary>
public partial class VoAliasPickerViewModel : ObservableObject
{
    private readonly IGameDataProvider _provider;
    private readonly string _voicesRoot;
    private List<VoAliasPickerRow> _allRows = [];

    public IReadOnlyList<ConversationFile> AllConversations { get; }
    public ObservableCollection<ConversationFile> VisibleConversations { get; } = [];
    public ObservableCollection<VoAliasPickerRow> VisibleRows { get; } = [];

    [ObservableProperty] private string _conversationFilter = string.Empty;
    [ObservableProperty] private string _nodeFilter         = string.Empty;
    [ObservableProperty] private ConversationFile? _selectedConversation;
    [ObservableProperty] private VoAliasPickerRow? _selectedRow;

    /// The alias the dialog returns; null until a pickable row is selected.
    public string? ResultAlias => SelectedRow?.DerivedAlias;

    public VoAliasPickerViewModel(IGameDataProvider provider, string gameRoot, string? currentAlias)
    {
        _provider   = provider;
        _voicesRoot = VoPathResolver.VoicesRoot(gameRoot);
        AllConversations = provider.EnumerateConversations()
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        RefreshVisibleConversations();

        // "Change…" pre-selects the current target when the alias parses.
        if (VoAliasParse.TryParse(currentAlias) is { } target)
        {
            SelectedConversation = AllConversations.FirstOrDefault(f =>
                string.Equals(f.Name, target.Conversation, StringComparison.OrdinalIgnoreCase));
            SelectedRow = _allRows.FirstOrDefault(r => r.NodeId == target.NodeId);
        }
    }

    partial void OnConversationFilterChanged(string value) => RefreshVisibleConversations();
    partial void OnNodeFilterChanged(string value)         => RefreshVisibleRows();
    partial void OnSelectedRowChanged(VoAliasPickerRow? value)
        => OnPropertyChanged(nameof(ResultAlias));

    partial void OnSelectedConversationChanged(ConversationFile? value)
    {
        SelectedRow = null;
        _allRows = [];
        if (value is not null)
        {
            try
            {
                var conv = _provider.LoadConversation(value);
                _allRows = conv.Nodes.Select(n =>
                {
                    var derived = VoPathResolver.ExpectedRelativePath(
                        n.SpeakerGuid, "", n.NodeId, value.Name);
                    var wem = derived is not null
                              && File.Exists(Path.Combine(_voicesRoot, derived + ".wem"));
                    var speaker = SpeakerNameService.FindByGuid(n.SpeakerGuid)?.Name
                                  ?? n.SpeakerCategory.ToString();
                    var text = conv.Strings.Get(n.NodeId)?.DefaultText ?? string.Empty;
                    return new VoAliasPickerRow(n.NodeId, speaker, text, derived, wem);
                }).ToList();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Alias picker: could not load '{value.Name}': {ex.Message}");
            }
        }
        RefreshVisibleRows();
    }

    private void RefreshVisibleConversations()
    {
        VisibleConversations.Clear();
        foreach (var f in AllConversations)
            if (string.IsNullOrWhiteSpace(ConversationFilter)
                || f.Name.Contains(ConversationFilter, StringComparison.OrdinalIgnoreCase))
                VisibleConversations.Add(f);
    }

    private void RefreshVisibleRows()
    {
        VisibleRows.Clear();
        foreach (var r in _allRows)
            if (string.IsNullOrWhiteSpace(NodeFilter)
                || r.TextPreview.Contains(NodeFilter, StringComparison.OrdinalIgnoreCase)
                || r.NodeId.ToString().Contains(NodeFilter, StringComparison.Ordinal))
                VisibleRows.Add(r);
    }
}
```

(If `AppLog` needs a `using`, copy it from another ViewModels file.)

- [x] **Step 5: Run tests, fix, run full suite, commit**

Run: `dotnet test --nologo --filter "FullyQualifiedName~VoAliasPickerViewModelTests"` → PASS (7 tests), then `dotnet test --nologo` → all pass.

```bash
git add DialogEditor.ViewModels/ViewModels/VoAliasPickerViewModel.cs DialogEditor.ViewModels/Services/VoPathResolver.cs DialogEditor.Tests/ViewModels/VoAliasPickerViewModelTests.cs
git commit -m "feat(vo): alias picker viewmodel with derived canonical paths

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: `VoAliasPickerWindow` + wiring

**Files:**
- Create: `DialogEditor.Avalonia/Views/VoAliasPickerWindow.axaml`
- Create: `DialogEditor.Avalonia/Views/VoAliasPickerWindow.axaml.cs`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (next to the `vm.Detail.ShowImportDialog = async paths =>` assignment at ~line 109)
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (expose provider)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`

**Interfaces:**
- Consumes: Task 6 `VoAliasPickerViewModel`, Task 3 `Detail.ShowAliasPicker`.
- Produces: `MainWindowViewModel.CurrentProvider` (`IGameDataProvider?`, get-only, returns the private provider field).

- [x] **Step 1: Add strings**

Append to the alias block in `Strings.axaml`:

```xml
    <sys:String x:Key="AliasPicker_Title">Reuse a Voice-Over Recording</sys:String>
    <sys:String x:Key="AliasPicker_ConversationsHeader">Conversation</sys:String>
    <sys:String x:Key="AliasPicker_NodesHeader">Nodes</sys:String>
    <sys:String x:Key="AliasPicker_NodeColumn">Node</sys:String>
    <sys:String x:Key="AliasPicker_SpeakerColumn">Speaker</sys:String>
    <sys:String x:Key="AliasPicker_TextColumn">Text</sys:String>
    <sys:String x:Key="AliasPicker_WemColumn">VO file</sys:String>
    <sys:String x:Key="AliasPicker_OkButton">Use this recording</sys:String>
    <sys:String x:Key="AliasPicker_CancelButton">Cancel</sys:String>
    <sys:String x:Key="Placeholder_AliasPickerConvFilter">Filter conversations…</sys:String>
    <sys:String x:Key="Placeholder_AliasPickerNodeFilter">Filter nodes by text or ID…</sys:String>
    <sys:String x:Key="ToolTip_AliasPickerConvList">Pick the conversation containing the line whose recording you want to reuse.</sys:String>
    <sys:String x:Key="ToolTip_AliasPickerNodeList">Pick the line whose recording this node should play. Greyed-out rows belong to speakers with no known voice folder. ✓/✗ shows whether the recording file exists.</sys:String>
    <sys:String x:Key="ToolTip_AliasPickerOk">Set this node's ExternalVO to the selected line's recording.</sys:String>
    <sys:String x:Key="ToolTip_AliasPickerConvFilter">Type to narrow the conversation list.</sys:String>
    <sys:String x:Key="ToolTip_AliasPickerNodeFilter">Type to narrow the node list by dialogue text or node ID.</sys:String>
```

- [x] **Step 2: Create the window**

`DialogEditor.Avalonia/Views/VoAliasPickerWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        x:Class="DialogEditor.Avalonia.Views.VoAliasPickerWindow"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="820" Height="520" MinWidth="640" MinHeight="380"
        WindowStartupLocation="CenterOwner"
        Title="{DynamicResource AliasPicker_Title}">

    <Grid ColumnDefinitions="260,8,*" RowDefinitions="Auto,*,Auto" Margin="8">

        <!-- Conversation pane -->
        <TextBox Grid.Row="0" Grid.Column="0"
                 Text="{Binding ConversationFilter, Mode=TwoWay}"
                 Watermark="{DynamicResource Placeholder_AliasPickerConvFilter}"
                 ToolTip.Tip="{DynamicResource ToolTip_AliasPickerConvFilter}"
                 AutomationProperties.Name="{DynamicResource Placeholder_AliasPickerConvFilter}"
                 AutomationProperties.HelpText="{DynamicResource ToolTip_AliasPickerConvFilter}"
                 Margin="0,0,0,4"/>
        <ListBox Grid.Row="1" Grid.Column="0"
                 ItemsSource="{Binding VisibleConversations}"
                 SelectedItem="{Binding SelectedConversation, Mode=TwoWay}"
                 ToolTip.Tip="{DynamicResource ToolTip_AliasPickerConvList}"
                 AutomationProperties.Name="{DynamicResource AliasPicker_ConversationsHeader}"
                 AutomationProperties.HelpText="{DynamicResource ToolTip_AliasPickerConvList}">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <TextBlock Text="{Binding Name}" TextTrimming="CharacterEllipsis"/>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>

        <!-- Node pane -->
        <TextBox Grid.Row="0" Grid.Column="2"
                 Text="{Binding NodeFilter, Mode=TwoWay}"
                 Watermark="{DynamicResource Placeholder_AliasPickerNodeFilter}"
                 ToolTip.Tip="{DynamicResource ToolTip_AliasPickerNodeFilter}"
                 AutomationProperties.Name="{DynamicResource Placeholder_AliasPickerNodeFilter}"
                 AutomationProperties.HelpText="{DynamicResource ToolTip_AliasPickerNodeFilter}"
                 Margin="0,0,0,4"/>
        <DockPanel Grid.Row="1" Grid.Column="2">
            <!-- Column headers; widths must match the row template below -->
            <Grid DockPanel.Dock="Top" ColumnDefinitions="60,140,*,60" Margin="8,0,0,2">
                <TextBlock Grid.Column="0" Text="{DynamicResource AliasPicker_NodeColumn}"
                           FontSize="{DynamicResource FontSize.Small}"
                           Foreground="{DynamicResource Brush.Text.Secondary}"/>
                <TextBlock Grid.Column="1" Text="{DynamicResource AliasPicker_SpeakerColumn}"
                           FontSize="{DynamicResource FontSize.Small}"
                           Foreground="{DynamicResource Brush.Text.Secondary}"/>
                <TextBlock Grid.Column="2" Text="{DynamicResource AliasPicker_TextColumn}"
                           FontSize="{DynamicResource FontSize.Small}"
                           Foreground="{DynamicResource Brush.Text.Secondary}"/>
                <TextBlock Grid.Column="3" Text="{DynamicResource AliasPicker_WemColumn}"
                           FontSize="{DynamicResource FontSize.Small}"
                           Foreground="{DynamicResource Brush.Text.Secondary}"/>
            </Grid>
            <ListBox ItemsSource="{Binding VisibleRows}"
                     SelectedItem="{Binding SelectedRow, Mode=TwoWay}"
                     DoubleTapped="Rows_DoubleTapped"
                     ToolTip.Tip="{DynamicResource ToolTip_AliasPickerNodeList}"
                     AutomationProperties.Name="{DynamicResource AliasPicker_NodesHeader}"
                     AutomationProperties.HelpText="{DynamicResource ToolTip_AliasPickerNodeList}">
                <ListBox.ItemTemplate>
                    <DataTemplate x:DataType="vm:VoAliasPickerRow">
                        <Grid ColumnDefinitions="60,140,*,60" Margin="0,1"
                              Opacity="{Binding IsPickable, Converter={StaticResource BoolToOpacity}}">
                            <TextBlock Grid.Column="0" Text="{Binding NodeId}" VerticalAlignment="Center"/>
                            <TextBlock Grid.Column="1" Text="{Binding SpeakerName}"
                                       TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
                            <TextBlock Grid.Column="2" Text="{Binding TextPreview}"
                                       TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
                            <TextBlock Grid.Column="3" Text="{Binding WemGlyph}"
                                       HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </DockPanel>

        <!-- Footer -->
        <StackPanel Grid.Row="2" Grid.Column="0" Grid.ColumnSpan="3"
                    Orientation="Horizontal" HorizontalAlignment="Right"
                    Spacing="8" Margin="0,8,0,0">
            <Button x:Name="OkButton"
                    Content="{DynamicResource AliasPicker_OkButton}"
                    Click="Ok_Click" IsDefault="True"
                    IsEnabled="{Binding SelectedRow.IsPickable, FallbackValue=False}"
                    ToolTip.Tip="{DynamicResource ToolTip_AliasPickerOk}"
                    AutomationProperties.HelpText="{DynamicResource ToolTip_AliasPickerOk}"/>
            <Button Content="{DynamicResource AliasPicker_CancelButton}"
                    Click="Cancel_Click" IsCancel="True"/>
        </StackPanel>
    </Grid>
</Window>
```

Check that a `BoolToOpacity` converter exists in app resources (`grep -rn "BoolToOpacity" DialogEditor.Avalonia`); if not, replace the `Opacity` binding with `IsEnabled="{Binding IsPickable}"` on the row Grid (ListBoxItem stays selectable but OK stays disabled via `SelectedRow.IsPickable`) — the OK gate is the actual enforcement either way.

`DialogEditor.Avalonia/Views/VoAliasPickerWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class VoAliasPickerWindow : Window
{
    public string? ResultAlias { get; private set; }

    public VoAliasPickerWindow() => InitializeComponent();   // XAML previewer

    public VoAliasPickerWindow(VoAliasPickerViewModel vm) : this()
        => DataContext = vm;

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        ResultAlias = (DataContext as VoAliasPickerViewModel)?.ResultAlias;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void Rows_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((DataContext as VoAliasPickerViewModel)?.ResultAlias is not null)
            Ok_Click(sender, e);
    }
}
```

- [x] **Step 3: Expose the provider and wire the delegate**

In `MainWindowViewModel.cs`, next to the other public properties, add:

```csharp
    /// Exposed for view wiring that needs read-only game data (alias picker).
    public IGameDataProvider? CurrentProvider => _provider;
```

(match the actual private field name — it is `_provider` as assigned at ~line 1082).

In `MainWindow.axaml.cs`, directly after the `vm.Detail.ShowImportDialog = async paths => ...` assignment block (~line 109), add:

```csharp
        vm.Detail.ShowAliasPicker = async currentAlias =>
        {
            if (vm.CurrentProvider is null || vm.Detail.GameRoot is not { Length: > 0 } root)
                return null;
            var picker = new VoAliasPickerWindow(
                new VoAliasPickerViewModel(vm.CurrentProvider, root, currentAlias));
            await picker.ShowDialog(this);
            return picker.ResultAlias;
        };
```

(`Detail.GameRoot` is the property set at ~`MainWindowViewModel.cs:1109`; if its accessibility differs, thread the root the same way `ShowImportDialog`'s wiring obtains paths.)

- [x] **Step 4: Build, test, commit**

```
dotnet build && dotnet test --nologo
```
Expected: build success, all tests pass.

```bash
git add DialogEditor.Avalonia/Views/VoAliasPickerWindow.axaml DialogEditor.Avalonia/Views/VoAliasPickerWindow.axaml.cs "DialogEditor.Avalonia/Views/MainWindow.axaml.cs" DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs "DialogEditor.Avalonia/Resources/Strings.axaml"
git commit -m "feat(vo): alias picker window wired into detail pane

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: Single-node import guard

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs` (`ImportVo`, ~line 202)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/ViewModels/NodeDetailViewModelPaneTests.cs` (extend)

**Interfaces:**
- Produces (Task 9's dialog consumes these exact names):
  - `enum VoAliasImportChoice { Cancel, OverwriteShared, ClearAliasImportOwn }`
  - `record VoAliasImportPrompt(string TargetPath, int SharedWithOthers)`
  - `Func<VoAliasImportPrompt, Task<VoAliasImportChoice>>? ConfirmAliasedImport { get; set; }` on `NodeDetailViewModel`

- [x] **Step 1: Write the failing tests**

Append to `NodeDetailViewModelPaneTests.cs`. The tests need a recording fake importer and import dialog; follow the fakes used by `NodeDetailViewModelPlaybackTests.cs` if present, otherwise define locally:

```csharp
    // ── Import guard on aliased nodes ────────────────────────────────────

    private sealed class RecordingImporter : IVoImporter
    {
        public VoImportRequest? LastRequest;
        public Task<VoImportResult> ImportAsync(VoImportRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new VoImportResult(true, null));
        }
    }

    [Fact]
    public async Task ImportVo_Aliased_CancelChoice_DoesNotImport()
    {
        var importer = ArrangeAliasedImport(out var prompts);
        _vm.ConfirmAliasedImport = p => { prompts.Add(p); return Task.FromResult(VoAliasImportChoice.Cancel); };

        await _vm.ImportVoCommand.ExecuteAsync(null);

        Assert.Single(prompts);
        Assert.Null(importer.LastRequest);
    }

    [Fact]
    public async Task ImportVo_Aliased_OverwriteChoice_ImportsToSharedPath()
    {
        var importer = ArrangeAliasedImport(out _);
        _vm.ConfirmAliasedImport = _ => Task.FromResult(VoAliasImportChoice.OverwriteShared);

        await _vm.ImportVoCommand.ExecuteAsync(null);

        Assert.NotNull(importer.LastRequest);
        Assert.Contains("other_conv_0005", importer.LastRequest!.DestPrimaryPath);
    }

    [Fact]
    public async Task ImportVo_Aliased_ClearChoice_ClearsAliasAndImportsOwnPath()
    {
        var importer = ArrangeAliasedImport(out _);
        _vm.ConfirmAliasedImport = _ => Task.FromResult(VoAliasImportChoice.ClearAliasImportOwn);

        await _vm.ImportVoCommand.ExecuteAsync(null);

        Assert.False(_vm.HasVoAlias);
        Assert.NotNull(importer.LastRequest);
        Assert.DoesNotContain("other_conv_0005", importer.LastRequest!.DestPrimaryPath);
    }

    [Fact]
    public async Task ImportVo_NotAliased_NoPromptShown()
    {
        var importer = ArrangeAliasedImport(out var prompts, externalVO: "");
        _vm.ConfirmAliasedImport = p => { prompts.Add(p); return Task.FromResult(VoAliasImportChoice.Cancel); };

        await _vm.ImportVoCommand.ExecuteAsync(null);

        Assert.Empty(prompts);
        Assert.NotNull(importer.LastRequest);
    }
```

`ArrangeAliasedImport` is a private helper to add in the test class: sets PoE2 context (as in Task 3's `LoadPoe2Node`), a saved `ProjectPath` (temp file path — check which member `ImportVo` reads, `ProjectPath` is referenced at `NodeDetailViewModel.cs:207`), a speaker GUID registered in `ChatterPrefixService` so the clear-alias path resolves, `Importer = new RecordingImporter()`, and `ShowImportDialog = _ => Task.FromResult<VoImportDialogResult?>(new VoImportDialogResult(<primary source>, null, WemQuality.Medium))` (match the record's real shape — read its definition first), then `LoadPoe2Node(externalVO: externalVO)` with default `"narrator/other_conv_0005"`. It returns the importer and outputs an empty prompt list.

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~NodeDetailViewModelPaneTests"`
Expected: **build failure** — `'VoAliasImportChoice' could not be found`.

- [x] **Step 3: Implement**

In `NodeDetailViewModel.cs` (file scope, next to `VoAliasUse`):

```csharp
/// Outcome of the aliased-import confirmation.
public enum VoAliasImportChoice { Cancel, OverwriteShared, ClearAliasImportOwn }

/// What the confirmation dialog shows: the shared target and its blast radius.
public record VoAliasImportPrompt(string TargetPath, int SharedWithOthers);
```

In the class, next to `ShowImportDialog`:

```csharp
    /// Set by MainWindow.axaml.cs — asks the user what to do when importing over
    /// an ExternalVO alias (the target file is shared with other nodes).
    public Func<VoAliasImportPrompt, Task<VoAliasImportChoice>>? ConfirmAliasedImport { get; set; }
```

In `ImportVo()`, after the `ProjectPath is null` early-return and **before** the fresh-node `HasVO` auto-set block, insert:

```csharp
        // Guard: the alias target is shared audio — importing overwrites it for
        // every node that aliases it (audit 2026-07-03: up to 11 nodes share one
        // file in shipped data). Confirm, or clear the alias and give this node
        // its own recording.
        if (HasVoAlias && ConfirmAliasedImport is not null)
        {
            var choice = await ConfirmAliasedImport(new VoAliasImportPrompt(
                _node!.ExternalVO, VoAliasSharedCount ?? 0));
            if (choice == VoAliasImportChoice.Cancel) return;
            if (choice == VoAliasImportChoice.ClearAliasImportOwn)
            {
                _node.ExternalVO = string.Empty;   // undoable
                _node.HasVO = true;
                // Re-resolve inline so the destination below uses the own path.
                _voCheck = VoPathResolver.Check(
                    _node.SpeakerGuid, true, _node.ExternalVO, _node.HasFemaleText, _node.NodeId,
                    Canvas?.ConversationName ?? "", GameRoot, ActiveGameId);
            }
        }
```

Add strings to `Strings.axaml` (alias block) for Task 9's dialog now so keys exist:

```xml
    <sys:String x:Key="AliasImport_Title">Shared Recording</sys:String>
    <!-- {0} = alias path, {1} = count of other nodes -->
    <sys:String x:Key="AliasImport_Message">This node plays a shared recording ({0}), also used by {1} other node(s). Importing will replace that recording for all of them.</sys:String>
    <sys:String x:Key="AliasImport_Overwrite">Overwrite the shared recording</sys:String>
    <sys:String x:Key="AliasImport_ClearAndOwn">Give this node its own recording</sys:String>
    <sys:String x:Key="AliasImport_Cancel">Cancel</sys:String>
    <sys:String x:Key="ToolTip_AliasImport_Overwrite">Replace the shared file. Every node aliasing it will play the new audio.</sys:String>
    <sys:String x:Key="ToolTip_AliasImport_ClearAndOwn">Remove the alias from this node and import to its own canonical file. Other nodes keep the shared recording.</sys:String>
```

- [x] **Step 4: Run tests, full suite, commit**

Run: `dotnet test --nologo --filter "FullyQualifiedName~NodeDetailViewModelPaneTests"` → PASS, then `dotnet test --nologo` → all pass.

```bash
git add DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs DialogEditor.Tests/ViewModels/NodeDetailViewModelPaneTests.cs "DialogEditor.Avalonia/Resources/Strings.axaml"
git commit -m "feat(vo): three-way confirm before importing over a shared alias

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 9: Alias-import confirm dialog window

**Files:**
- Create: `DialogEditor.Avalonia/Views/AliasImportConfirmDialog.axaml`
- Create: `DialogEditor.Avalonia/Views/AliasImportConfirmDialog.axaml.cs`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (next to the Task 7 wiring)

**Interfaces:**
- Consumes: Task 8 `VoAliasImportChoice`, `VoAliasImportPrompt`, `Detail.ConfirmAliasedImport`.

- [x] **Step 1: Create the dialog**

`DialogEditor.Avalonia/Views/AliasImportConfirmDialog.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.AliasImportConfirmDialog"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="480" SizeToContent="Height" CanResize="False"
        WindowStartupLocation="CenterOwner"
        Title="{DynamicResource AliasImport_Title}">
    <StackPanel Margin="16" Spacing="12">
        <TextBlock x:Name="MessageText" TextWrapping="Wrap"/>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
            <Button Content="{DynamicResource AliasImport_Overwrite}"
                    Click="Overwrite_Click"
                    ToolTip.Tip="{DynamicResource ToolTip_AliasImport_Overwrite}"
                    AutomationProperties.HelpText="{DynamicResource ToolTip_AliasImport_Overwrite}"/>
            <Button Content="{DynamicResource AliasImport_ClearAndOwn}"
                    Click="ClearAndOwn_Click"
                    ToolTip.Tip="{DynamicResource ToolTip_AliasImport_ClearAndOwn}"
                    AutomationProperties.HelpText="{DynamicResource ToolTip_AliasImport_ClearAndOwn}"/>
            <Button Content="{DynamicResource AliasImport_Cancel}"
                    Click="Cancel_Click" IsCancel="True" IsDefault="True"/>
        </StackPanel>
    </StackPanel>
</Window>
```

(Cancel is `IsDefault` deliberately — Enter must not overwrite shared audio. OK/Cancel-style buttons need no tooltip per project rules, but the two consequence-bearing choices carry them.)

`DialogEditor.Avalonia/Views/AliasImportConfirmDialog.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Views;

public partial class AliasImportConfirmDialog : Window
{
    public VoAliasImportChoice Choice { get; private set; } = VoAliasImportChoice.Cancel;

    public AliasImportConfirmDialog() => InitializeComponent();   // XAML previewer

    public AliasImportConfirmDialog(VoAliasImportPrompt prompt) : this()
        => MessageText.Text = Loc.Format("AliasImport_Message",
            prompt.TargetPath, prompt.SharedWithOthers);

    private void Overwrite_Click(object? s, RoutedEventArgs e)
        { Choice = VoAliasImportChoice.OverwriteShared; Close(); }
    private void ClearAndOwn_Click(object? s, RoutedEventArgs e)
        { Choice = VoAliasImportChoice.ClearAliasImportOwn; Close(); }
    private void Cancel_Click(object? s, RoutedEventArgs e)
        { Choice = VoAliasImportChoice.Cancel; Close(); }
}
```

(If `VoAliasImportChoice`/`VoAliasImportPrompt` ended up in a namespace other than `DialogEditor.ViewModels`, fix the `using`. If `Loc` lives elsewhere, match other dialog code-behind files' usings.)

- [x] **Step 2: Wire in MainWindow.axaml.cs**

Next to the Task 7 `ShowAliasPicker` wiring:

```csharp
        vm.Detail.ConfirmAliasedImport = async prompt =>
        {
            var dlg = new AliasImportConfirmDialog(prompt);
            await dlg.ShowDialog(this);
            return dlg.Choice;
        };
```

- [x] **Step 3: Build, test, commit**

```
dotnet build && dotnet test --nologo
```

```bash
git add DialogEditor.Avalonia/Views/AliasImportConfirmDialog.axaml DialogEditor.Avalonia/Views/AliasImportConfirmDialog.axaml.cs "DialogEditor.Avalonia/Views/MainWindow.axaml.cs"
git commit -m "feat(vo): alias-overwrite confirm dialog

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 10: Batch import excludes aliased rows

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/BatchVoImportViewModel.cs` (`BatchVoRowViewModel` ctor + import filter, lines ~64–75 and ~127)
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs` (row builder, ~line 692–705)
- Modify: `DialogEditor.Avalonia/Views/BatchVoImportDialog.axaml` (row template, lines ~137–201)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/ViewModels/BatchVoImportViewModelTests.cs` (extend if it exists — `ls DialogEditor.Tests/ViewModels | grep -i batch` — else create with only the new tests)

**Interfaces:**
- Produces: `BatchVoRowViewModel.IsAliased` (`bool`, init via ctor param `bool isAliased`), `BatchVoRowViewModel.StatusDisplay` (`string` — glyph, or the aliased marker).

- [x] **Step 1: Write the failing tests**

In the batch tests file (create `DialogEditor.Tests/ViewModels/BatchVoImportViewModelTests.cs` if absent, with `Loc.Configure(new StubStringProvider());` in the constructor):

```csharp
    [Fact]
    public void AliasedRow_StatusDisplay_ShowsAliasedMarker()
    {
        var row = new BatchVoRowViewModel("conv", 1, "text",
            VoPresence.Missing, "p.wem", "f.wem", isAliased: true);
        Assert.True(row.IsAliased);
        Assert.Equal("BatchVoImport_AliasedStatus", row.StatusDisplay);
    }

    [Fact]
    public void NonAliasedRow_StatusDisplay_ShowsGlyph()
    {
        var row = new BatchVoRowViewModel("conv", 1, "text",
            VoPresence.Found, "p.wem", "f.wem", isAliased: false);
        Assert.Equal("✓", row.StatusDisplay);
    }

    [Fact]
    public async Task Import_SkipsAliasedRows()
    {
        var aliased = new BatchVoRowViewModel("conv", 1, "t",
            VoPresence.Missing, "p1.wem", "f1.wem", isAliased: true)
            { PrimarySourcePath = "src1.wav" };
        var normal = new BatchVoRowViewModel("conv", 2, "t",
            VoPresence.Missing, "p2.wem", "f2.wem", isAliased: false)
            { PrimarySourcePath = "src2.wav" };
        var importer = new RecordingBatchImporter();   // reuse/define like Task 8's fake
        var vm = new BatchVoImportViewModel([aliased, normal], importer);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(["p2.wem"], importer.Destinations);
        Assert.Equal(BatchRowStatus.Pending, aliased.RowStatus);   // untouched
    }
```

(`RecordingBatchImporter` records `request.DestPrimaryPath` into `Destinations`; match `IVoImporter`'s real member names when writing it.)

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~BatchVoImportViewModelTests"`
Expected: **build failure** — no ctor overload with `isAliased`.

- [x] **Step 3: Implement**

In `BatchVoRowViewModel`:
- Add `public bool IsAliased { get; }` and a trailing ctor parameter `bool isAliased = false`, assigned in the body.
- Add, next to `VoStatusGlyph`:

```csharp
    /// Status cell: aliased rows are excluded from batch import — the target
    /// file is shared with other nodes; the single-node flow has the confirm.
    public string StatusDisplay => IsAliased
        ? Loc.Get("BatchVoImport_AliasedStatus")
        : VoStatusGlyph;
```

(add `using DialogEditor.ViewModels.Resources;` if missing — it is already there for `Loc` in the import loop.)

In `BatchVoImportViewModel.ImportAsync` change the filter (~line 127):

```csharp
        var toImport = AllRows.Where(r => r.HasPrimarySource && !r.IsAliased).ToList();
```

In `ConversationViewModel.cs`'s batch-row builder (the loop at ~692 that calls `VoPathResolver.Check` and then constructs `BatchVoRowViewModel`), pass the new argument to the constructor call a few lines below line 700:

```csharp
                isAliased: !string.IsNullOrEmpty(node.ExternalVO)
```

In `BatchVoImportDialog.axaml`:
- Status glyph cell (line ~138): change `Text="{Binding VoStatusGlyph}"` to `Text="{Binding StatusDisplay}"` and add `ToolTip.Tip="{DynamicResource ToolTip_BatchAliasedRow}"`.
- On the two Browse buttons (`BrowsePrimary_Click` line ~156, `BrowseFem_Click` line ~186) add `IsEnabled="{Binding !IsAliased}"`.

Add strings (batch block of `Strings.axaml`):

```xml
    <sys:String x:Key="BatchVoImport_AliasedStatus">shared</sys:String>
    <sys:String x:Key="ToolTip_BatchAliasedRow">This node plays a recording shared with other nodes (ExternalVO alias). Batch import skips it — use the node's own import button to deliberately replace or detach the shared audio.</sys:String>
```

Note: the status column is 50px — "shared" fits; keep the key's value short in translations too (comment in `Strings.axaml` if needed).

- [x] **Step 4: Run tests, full suite, commit**

Run: `dotnet test --nologo --filter "FullyQualifiedName~BatchVoImportViewModelTests"` → PASS, then `dotnet build && dotnet test --nologo` → all pass.

```bash
git add DialogEditor.ViewModels/ViewModels/BatchVoImportViewModel.cs DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs "DialogEditor.Avalonia/Views/BatchVoImportDialog.axaml" "DialogEditor.Avalonia/Resources/Strings.axaml" DialogEditor.Tests/ViewModels/BatchVoImportViewModelTests.cs
git commit -m "feat(vo): batch import skips shared-alias rows

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 11: Gaps.md follow-up + manual verification

**Files:**
- Modify: `Gaps.md` (the "VO import over an ExternalVO alias" entry)

- [x] **Step 1: Update Gaps.md**

The entry "VO import over an ExternalVO alias silently overwrites shared audio" is now fixed by this feature. Move nothing — `Gaps.md` has no Fixed section; instead rewrite the entry's gap paragraph to record the resolution ("Resolved 2026-07-03: single-node import shows a three-way confirm; batch import excludes aliased rows; alias edits go through the node picker") and keep the background sentence. Commit as `docs(gaps): mark ExternalVO alias hazard resolved`.

- [ ] **Step 2: Run the app** — `dotnet run --project DialogEditor.Avalonia`, open the Deadfire project.

- [ ] **Step 3: Walk the checklist**

- [ ] Open `08_cv_atsura` (79 aliased nodes): an aliased node's Voice group shows the friendly line, readonly raw path, and "Also used by N other node(s)" once the background index finishes
- [ ] ▶ play on an aliased node plays the target recording (unchanged behavior)
- [ ] "Reuse another line's VO…" appears only on non-aliased PoE2 nodes; opens the picker; conversation + node filters work; ✓/✗ column reflects real files; disabled rows for unknown speakers
- [ ] Picking a node writes the alias (undo reverts it); Change… pre-selects the current target; Clear removes the alias and the Voice group returns to the own-file framing
- [ ] 🎤 on an aliased node shows the three-way dialog; Cancel imports nothing; "Give this node its own recording" clears the alias and imports to `<conv>_<id>.wem`; Overwrite imports to the shared path
- [ ] Batch VO import: aliased rows show "shared", Browse buttons disabled, import skips them
- [ ] PoE1 project (if available): Voice group shows no alias UI at all
- [ ] Tab order and tooltips: every new control shows a tooltip; picker and confirm dialogs carry the app icon

- [ ] **Step 4: Report results** — any failure: fix in the owning task's files, `dotnet build && dotnet test --nologo`, re-verify, commit as `fix(vo): …`.
