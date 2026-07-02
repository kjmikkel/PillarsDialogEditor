# VO File Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Intent-driven female-VO reporting, a project-wide orphan scan with confirmed cleanup in the Validate Voice-Over window, and a node-ID reuse guard, per `docs/superpowers/specs/2026-07-02-vo-lifecycle-design.md`.

**Architecture:** A shared `ExpectedRelativePath` helper on `VoPathResolver` becomes the single source of VO naming. `Check`/`WithLocalVoFallback` gain a `hasFemaleText` gate. A new pure service `VoOrphanScanner` computes the expected-path set from the project (vanilla + patch, live canvas wins) and diffs it against `_vo/`. `VoValidationViewModel` runs the scanner via an injected delegate and owns an armed two-click cleanup. `NodeIdAllocator` accepts an `isReserved` predicate supplied by `ConversationViewModel`.

**Tech Stack:** .NET 8, Avalonia, CommunityToolkit.Mvvm, xUnit.

## Global Constraints

- Strict red/green TDD: failing test before any implementation code (CLAUDE.md).
- No user-visible text hard-coded — every new string goes in `DialogEditor.Avalonia/Resources/Strings.axaml` (CLAUDE.md).
- Every new interactive control carries a detailed `ToolTip` (CLAUDE.md).
- Every caught exception logged via `AppLog.Warn`/`AppLog.Error`; `OperationCanceledException` swallowed silently; no bare `catch { }` (CLAUDE.md).
- Tests run serially — do not change test parallelization settings.
- Run tests with: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~<name>" --nologo`
- The user's editor may hold a build lock (Visual Studio debug session). If MSB3021/MSB3026 "file is being used by another process" appears, stop and ask the user to close the editor — do not kill the process.

---

### Task 1: `VoPathResolver.ExpectedRelativePath` — single source of VO naming

**Files:**
- Modify: `DialogEditor.ViewModels/Services/VoPathResolver.cs`
- Test: `DialogEditor.Tests/Services/VoPathResolverTests.cs`

**Interfaces:**
- Produces: `public static string? ExpectedRelativePath(string speakerGuid, string externalVO, int nodeId, string conversationName)` — returns the `_vo/`-relative base path **without extension** (e.g. `eder\conv_0001` on Windows), or `null` when the speaker prefix is unknown and no `ExternalVO` is set. Task 3 consumes this.

- [ ] **Step 1: Write the failing tests** (append to `VoPathResolverTests`; the fixture already registers prefix `eder` for GUID `9c5f12c9-e93d-4952-9f1a-726c9498f8fb`)

```csharp
    // ── ExpectedRelativePath — canonical _vo/-relative naming ─────────────

    [Fact]
    public void ExpectedRelativePath_KnownPrefix_BuildsPrefixConvIdPath()
    {
        var rel = VoPathResolver.ExpectedRelativePath(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "", 7, "My_Conv");
        Assert.Equal(Path.Combine("eder", "my_conv_0007"), rel);
    }

    [Fact]
    public void ExpectedRelativePath_ExternalVO_UsedVerbatim()
    {
        var rel = VoPathResolver.ExpectedRelativePath(
            "unknown-guid", "eder/custom_line", 7, "My_Conv");
        Assert.Equal(Path.Combine("eder", "custom_line"), rel);
    }

    [Fact]
    public void ExpectedRelativePath_UnknownPrefixNoExternal_ReturnsNull()
    {
        var rel = VoPathResolver.ExpectedRelativePath("unknown-guid", "", 7, "conv");
        Assert.Null(rel);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ExpectedRelativePath" --nologo`
Expected: compile error CS0117 (`VoPathResolver` does not contain `ExpectedRelativePath`).

- [ ] **Step 3: Implement — extract the naming logic `Check` already uses**

Add to `VoPathResolver` (below `Check`), and refactor `Check`'s `basePath` computation to call it so the logic exists once:

```csharp
    /// <summary>
    /// Canonical _vo/-relative base path (no extension) for a node's VO:
    /// <c>ExternalVO</c> verbatim when set, otherwise
    /// <c>&lt;chatterPrefix&gt;/&lt;conversation&gt;_&lt;nodeId:0000&gt;</c>.
    /// Null when the speaker prefix is unknown and no ExternalVO is set.
    /// </summary>
    public static string? ExpectedRelativePath(
        string speakerGuid, string externalVO, int nodeId, string conversationName)
    {
        if (!string.IsNullOrEmpty(externalVO))
            return Path.Combine(externalVO.Split('/', '\\'));

        var chatterPrefix = string.Equals(speakerGuid, NarratorGuid,
                                StringComparison.OrdinalIgnoreCase)
                            ? "narrator"
                            : ChatterPrefixService.GetPrefix(speakerGuid);
        if (string.IsNullOrEmpty(chatterPrefix)) return null;

        return Path.Combine(
            chatterPrefix.ToLowerInvariant(),
            $"{conversationName.ToLowerInvariant()}_{nodeId:0000}");
    }
```

In `Check`, replace the `string basePath; if (...externalVO...) { ... } else { ... }` block with:

```csharp
        var relBase = ExpectedRelativePath(speakerGuid, externalVO, nodeId, conversationName);
        if (relBase is null)
            return new VoCheckResult(VoPresence.Missing, false, null, null);
        var basePath = Path.Combine(voRoot, relBase);
```

- [ ] **Step 4: Run the resolver test class — all green (refactor must not change `Check` behavior)**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~VoPathResolverTests" --nologo`
Expected: PASS, zero failures.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/VoPathResolver.cs DialogEditor.Tests/Services/VoPathResolverTests.cs
git commit -m "refactor(vo): extract ExpectedRelativePath as single source of VO naming"
```

---

### Task 2: Intent-driven female VO

**Files:**
- Modify: `DialogEditor.ViewModels/Services/VoPathResolver.cs` (`Check`, `WithLocalVoFallback`)
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs` (two `Check` call sites ~lines 220 and 503; one `WithLocalVoFallback` call site ~line 507)
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs` (`Check` call ~line 669 in the batch-VO row builder)
- Modify: `DialogEditor.ViewModels/ViewModels/VoValidationViewModel.cs` (`Check` call ~line 120)
- Test: `DialogEditor.Tests/Services/VoPathResolverTests.cs`, plus updates in `DialogEditor.Tests/ViewModels/NodeDetailViewModelPlaybackTests.cs` and any other test calling the changed signatures

**Interfaces:**
- Produces: `Check(string speakerGuid, bool hasVO, string externalVO, bool hasFemaleText, int nodeId, string conversationName, string gameRoot, string activeGameId)` — `hasFemaleText` inserted **after `externalVO`**. `FemaleVariantFound`/`FemWemPath` are only set when `hasFemaleText` is true AND the file exists.
- Produces: `WithLocalVoFallback(VoCheckResult result, string? projectPath, string gameRoot, bool hasFemaleText)` — same gate for the local `_fem.wem`.

- [ ] **Step 1: Write the failing test** (append to `VoPathResolverTests`)

```csharp
    [Fact]
    public void Check_FemFileExistsButNoFemaleText_FemVariantNotReported()
    {
        // Regression: a leftover _fem.wem must not be advertised as the node's
        // female variant when the node has no female text (design 2026-07-02).
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "conv_0001.wem"), "");
        File.WriteAllText(Path.Combine(dir, "conv_0001_fem.wem"), "");

        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", hasFemaleText: false,
            1, "conv", _gameRoot, "poe2")!;

        Assert.Equal(VoPresence.Found, result.Status);
        Assert.False(result.FemaleVariantFound);
        Assert.Null(result.FemWemPath);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Check_FemFileExistsButNoFemaleText" --nologo`
Expected: compile error CS1739/CS1501 (no `hasFemaleText` parameter).

- [ ] **Step 3: Implement the gate**

In `Check`, add the parameter after `externalVO`:

```csharp
    public static VoCheckResult? Check(
        string speakerGuid,
        bool   hasVO,
        string externalVO,
        bool   hasFemaleText,
        int    nodeId,
        string conversationName,
        string gameRoot,
        string activeGameId)
```

and gate the fem lookup (replace the `femExists` line):

```csharp
        var femExists = hasFemaleText && File.Exists(fem);
```

In `WithLocalVoFallback`, add `bool hasFemaleText` as the last parameter and gate:

```csharp
        var localFem  = localPrimary[..^4] + "_fem.wem";
        var femExists = hasFemaleText && File.Exists(localFem);
```

- [ ] **Step 4: Update every caller — compile errors are the worklist**

Production call sites (pass the node's intent):

```csharp
// NodeDetailViewModel ~line 220 (inline recheck in ImportVo):
_voCheck = VoPathResolver.Check(
    _node.SpeakerGuid, true, _node.ExternalVO, _node.HasFemaleText, _node.NodeId,
    Canvas?.ConversationName ?? "", GameRoot, ActiveGameId);

// NodeDetailViewModel ~line 503 (NotifyAllProxies):
: VoPathResolver.Check(
    _node.SpeakerGuid, _node.HasVO, _node.ExternalVO, _node.HasFemaleText, _node.NodeId,
    Canvas?.ConversationName ?? "", GameRoot, ActiveGameId);

// NodeDetailViewModel ~line 507 (fallback):
_voCheck = VoPathResolver.WithLocalVoFallback(_voCheck, ProjectPath, GameRoot,
    _node?.HasFemaleText ?? false);

// ConversationViewModel ~line 669 (batch rows; `node` is a NodeViewModel):
var check = VoPathResolver.Check(
    node.SpeakerGuid, node.HasVO, node.ExternalVO, node.HasFemaleText, node.NodeId,
    ConversationName, voGameRoot, activeGameId);   // keep the existing local arg names

// VoValidationViewModel ~line 120 (`node` is a NodeEditSnapshot):
var result = VoPathResolver.Check(
    node.SpeakerGuid, node.HasVO, node.ExternalVO, node.FemaleText.Length > 0,
    node.NodeId, _conversationName, _gameRoot, _activeGameId);
// and directly below, the fallback:
result = VoPathResolver.WithLocalVoFallback(result, _projectPath, _gameRoot,
    node.FemaleText.Length > 0);
```

Test call sites: fix every compile error by inserting the `hasFemaleText` argument after `externalVO`. Use `true` where the old behavior (fem checked) is what the test asserts, `false` otherwise. Two **deliberate behavior updates**:

1. `NodeDetailViewModelPlaybackTests.PlantAndLoad` — the fem tests plant a fem file but the node has no female text, which is now (correctly) invisible. Give the node female text when `withFem` is true:

```csharp
        _vm.Load(new NodeViewModel(node, new StringEntry(1, "Test line", withFem ? "Fem line" : "")));
```

2. In `VoValidationViewModelTests` and `ConversationViewModelBatchVoTests`, any test asserting fem-variant behavior must give its node/snapshot non-empty `FemaleText`. Tests asserting primary-only behavior need no data change.

- [ ] **Step 5: Run full suite**

Run: `dotnet test DialogEditor.Tests --nologo`
Expected: PASS, zero failures.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels DialogEditor.Tests
git commit -m "feat(vo): female VO reporting is intent-driven (node must have female text)"
```

---

### Task 3: `VoOrphanScanner`

**Files:**
- Create: `DialogEditor.ViewModels/Services/VoOrphanScanner.cs`
- Test: `DialogEditor.Tests/Services/VoOrphanScannerTests.cs`

**Interfaces:**
- Consumes: `VoPathResolver.ExpectedRelativePath` (Task 1), `PatchApplier.Apply`, `ConversationSnapshotBuilder.Build`, `IGameDataProvider.FindConversation/LoadConversation/Language`, `ChatterPrefixService`.
- Produces: `public static IReadOnlyList<string> FindOrphans(DialogProject project, IGameDataProvider provider, string projectPath, string? openConversationName = null, ConversationEditSnapshot? openSnapshot = null)` — full paths of orphaned `.wem` files under `<projectDir>/_vo`, sorted. Task 5 consumes this via a delegate.

- [ ] **Step 1: Write the failing tests**

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class VoOrphanScannerTests : IDisposable
{
    private readonly string _projectDir;
    private readonly string _voDir;
    private readonly string _projectPath;

    private const string SpeakerGuid = "9c5f12c9-e93d-4952-9f1a-726c9498f8fb";

    public VoOrphanScannerTests()
    {
        _projectDir  = Path.Combine(Path.GetTempPath(), $"OrphanTest_{Guid.NewGuid():N}");
        _voDir       = Path.Combine(_projectDir, "_vo");
        _projectPath = Path.Combine(_projectDir, "test.dialogproject");
        Directory.CreateDirectory(_voDir);

        ChatterPrefixService.Register(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { SpeakerGuid, "eder" }
        });
    }

    public void Dispose()
    {
        ChatterPrefixService.Clear();
        try { Directory.Delete(_projectDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private void PlantWem(string relative)
    {
        var full = Path.Combine(_voDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "");
    }

    private static ConversationNode MakeNode(
        int id, bool hasVO = true, string externalVO = "", string speaker = SpeakerGuid) =>
        new(id, false, SpeakerCategory.Npc, speaker, "", [],
            [], [], "Conversation", "None",
            ActorDirection: "", Comments: "", ExternalVO: externalVO,
            HasVO: hasVO, HideSpeaker: false);

    /// Provider with one conversation "conv" containing the given nodes;
    /// femaleTextIds get non-empty female text in the string table.
    private static FakeGameDataProvider Provider(
        IReadOnlyList<ConversationNode> nodes, params int[] femaleTextIds)
    {
        var entries = nodes.Select(n => new StringEntry(
            n.NodeId, "line", femaleTextIds.Contains(n.NodeId) ? "fem line" : "")).ToList();
        return new FakeGameDataProvider("poe2", "en",
            new Conversation("conv", nodes, new StringTable(entries)));
    }

    /// Project with an (empty) patch for "conv" so the scanner considers it.
    private static DialogProject ProjectWithConvPatch() =>
        DialogProject.Empty("P").WithPatch(
            new ConversationPatch("conv", ConversationPatch.CurrentSchemaVersion, [], [], []));

    [Fact]
    public void ReferencedFile_NotAnOrphan()
    {
        PlantWem(Path.Combine("eder", "conv_0001.wem"));
        var provider = Provider([MakeNode(1)]);

        var orphans = VoOrphanScanner.FindOrphans(ProjectWithConvPatch(), provider, _projectPath);

        Assert.Empty(orphans);
    }

    [Fact]
    public void FileForDeletedNode_IsOrphan()
    {
        PlantWem(Path.Combine("eder", "conv_0001.wem"));
        PlantWem(Path.Combine("eder", "conv_0099.wem"));   // node 99 does not exist
        var provider = Provider([MakeNode(1)]);

        var orphans = VoOrphanScanner.FindOrphans(ProjectWithConvPatch(), provider, _projectPath);

        var orphan = Assert.Single(orphans);
        Assert.EndsWith("conv_0099.wem", orphan);
    }

    [Fact]
    public void FemFileWithoutFemaleText_IsOrphan()
    {
        PlantWem(Path.Combine("eder", "conv_0001.wem"));
        PlantWem(Path.Combine("eder", "conv_0001_fem.wem"));
        var provider = Provider([MakeNode(1)]);   // node 1 has NO female text

        var orphans = VoOrphanScanner.FindOrphans(ProjectWithConvPatch(), provider, _projectPath);

        var orphan = Assert.Single(orphans);
        Assert.EndsWith("_fem.wem", orphan);
    }

    [Fact]
    public void FemFileWithFemaleText_NotAnOrphan()
    {
        PlantWem(Path.Combine("eder", "conv_0001.wem"));
        PlantWem(Path.Combine("eder", "conv_0001_fem.wem"));
        var provider = Provider([MakeNode(1)], femaleTextIds: 1);

        var orphans = VoOrphanScanner.FindOrphans(ProjectWithConvPatch(), provider, _projectPath);

        Assert.Empty(orphans);
    }

    [Fact]
    public void FileForConversationWithoutPatch_IsOrphan()
    {
        // "removedconv" was dropped from the project; its files are orphans.
        PlantWem(Path.Combine("eder", "removedconv_0001.wem"));
        var provider = Provider([MakeNode(1)]);

        var orphans = VoOrphanScanner.FindOrphans(ProjectWithConvPatch(), provider, _projectPath);

        var orphan = Assert.Single(orphans);
        Assert.EndsWith("removedconv_0001.wem", orphan);
    }

    [Fact]
    public void ExternalVoReferencedFile_NotAnOrphan()
    {
        PlantWem(Path.Combine("eder", "custom_take.wem"));
        var provider = Provider([MakeNode(1, hasVO: false, externalVO: "eder/custom_take")]);

        var orphans = VoOrphanScanner.FindOrphans(ProjectWithConvPatch(), provider, _projectPath);

        Assert.Empty(orphans);
    }

    [Fact]
    public void OpenCanvasSnapshot_WinsOverSavedState()
    {
        PlantWem(Path.Combine("eder", "conv_0001.wem"));
        PlantWem(Path.Combine("eder", "conv_0005.wem"));   // node 5 exists only on the canvas
        var provider = Provider([MakeNode(1)]);
        var canvas = ConversationSnapshotBuilder.Build(new Conversation("conv",
            [MakeNode(1), MakeNode(5)],
            new StringTable([new StringEntry(1, "line", ""), new StringEntry(5, "new line", "")])));

        var orphans = VoOrphanScanner.FindOrphans(
            ProjectWithConvPatch(), provider, _projectPath,
            openConversationName: "conv", openSnapshot: canvas);

        Assert.Empty(orphans);
    }
}
```

Note: `FakeGameDataProvider.FindConversation` — check `DialogEditor.Tests/Helpers/FakeGameDataProvider.cs`; if `FindConversation` is an interface default returning lookups over `EnumerateConversations()`, nothing to do. If it throws `NotSupportedException`, implement it in the fake:

```csharp
    public ConversationFile? FindConversation(string name)
        => _conversations.ContainsKey(name) ? BuildNewConversationFile(name) : null;
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~VoOrphanScannerTests" --nologo`
Expected: compile error CS0246 (`VoOrphanScanner` not found).

- [ ] **Step 3: Implement**

Create `DialogEditor.ViewModels/Services/VoOrphanScanner.cs`:

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Finds .wem files in the project's _vo/ staging folder that no VO-enabled node
/// references (design: docs/superpowers/specs/2026-07-02-vo-lifecycle-design.md).
/// The expected set is computed project-wide: every patched conversation is loaded
/// (vanilla + patch, conflicts ignored — display semantics), and the conversation
/// open on the canvas is represented by its live snapshot so unsaved edits count.
/// A _fem.wem is only expected when the node has female text (intent-driven).
/// </summary>
public static class VoOrphanScanner
{
    public static IReadOnlyList<string> FindOrphans(
        DialogProject project,
        IGameDataProvider provider,
        string projectPath,
        string? openConversationName = null,
        ConversationEditSnapshot? openSnapshot = null)
    {
        var voDir = Path.Combine(Path.GetDirectoryName(projectPath)!, "_vo");
        if (!Directory.Exists(voDir)) return [];

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (convName, patch) in project.Patches)
        {
            ConversationEditSnapshot snap;
            IReadOnlyDictionary<int, string> femOverride;

            if (convName == openConversationName && openSnapshot is not null)
            {
                // Live canvas text is authoritative for the open conversation.
                snap        = openSnapshot;
                femOverride = new Dictionary<int, string>();
            }
            else
            {
                try
                {
                    var file     = provider.FindConversation(convName);
                    var baseSnap = file is not null
                        ? ConversationSnapshotBuilder.Build(provider.LoadConversation(file))
                        : new ConversationEditSnapshot([]);
                    snap = PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true);
                }
                catch (Exception ex)
                {
                    // Unreadable conversation: skip rather than flag its files.
                    AppLog.Warn($"Orphan scan: could not load '{convName}': {ex.Message}");
                    continue;
                }
                // Added nodes carry no text in the snapshot ([JsonIgnore]) — female
                // text for them lives in the patch's translations.
                femOverride = (patch.Translations.GetValueOrDefault(provider.Language) ?? [])
                    .ToDictionary(t => t.NodeId, t => t.FemaleText);
            }

            foreach (var node in snap.Nodes)
            {
                if (!node.HasVO && string.IsNullOrEmpty(node.ExternalVO)) continue;

                var relBase = VoPathResolver.ExpectedRelativePath(
                    node.SpeakerGuid, node.ExternalVO, node.NodeId, convName);
                if (relBase is null) continue;   // unknown speaker — claim nothing

                expected.Add(relBase + ".wem");
                var femText = femOverride.TryGetValue(node.NodeId, out var t)
                    ? t : node.FemaleText;
                if (!string.IsNullOrEmpty(femText))
                    expected.Add(relBase + "_fem.wem");
            }
        }

        return Directory.EnumerateFiles(voDir, "*.wem", SearchOption.AllDirectories)
            .Where(f => !expected.Contains(Path.GetRelativePath(voDir, f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
```

- [ ] **Step 4: Run the scanner tests**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~VoOrphanScannerTests" --nologo`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/VoOrphanScanner.cs DialogEditor.Tests/Services/VoOrphanScannerTests.cs DialogEditor.Tests/Helpers/FakeGameDataProvider.cs
git commit -m "feat(vo): project-wide orphan scanner for the _vo/ staging folder"
```

---

### Task 4: Node-ID reuse guard

**Files:**
- Modify: `DialogEditor.Core/Editing/NodeIdAllocator.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs` (`AddConnectedNode` ~line 399; add `NextNodeId()`)
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml.cs` (two `NodeIdAllocator.Next` call sites ~lines 260 and 301 → `vm.NextNodeId()`)
- Test: `DialogEditor.Tests/Editing/NodeIdAllocatorTests.cs` (create if absent; check `DialogEditor.Tests` for an existing allocator test file first and extend it instead), `DialogEditor.Tests/ViewModels/ConversationViewModelEditTests.cs`

**Interfaces:**
- Produces: `NodeIdAllocator.Next(IEnumerable<int> existingIds, Func<int,bool>? isReserved = null)`; `ConversationViewModel.NextNodeId()` — public, used by the canvas code-behind.

- [ ] **Step 1: Write the failing allocator test**

```csharp
    [Fact]
    public void Next_SkipsReservedIds()
    {
        // A _vo/ file may exist for a deleted node's ID; reusing the ID would
        // silently attach the old audio to the new node (B-005 family).
        var next = NodeIdAllocator.Next([1, 2, 3], isReserved: id => id is 4 or 5);
        Assert.Equal(6, next);
    }
```

- [ ] **Step 2: Run to verify failure** — expected: compile error CS1739 (no `isReserved`).

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Next_SkipsReservedIds" --nologo`

- [ ] **Step 3: Implement**

```csharp
public static class NodeIdAllocator
{
    /// <param name="isReserved">
    /// Optional veto for otherwise-free IDs. Used to skip IDs whose _vo/ VO file
    /// still exists from a deleted node, so new nodes never silently inherit audio.
    /// </param>
    public static int Next(IEnumerable<int> existingIds, Func<int, bool>? isReserved = null)
    {
        var ids = existingIds.ToList();
        var candidate = ids.Count == 0 ? 1 : ids.Max() + 1;
        while (isReserved?.Invoke(candidate) == true) candidate++;
        return candidate;
    }
}
```

- [ ] **Step 4: Write the failing ConversationViewModel test** (append to `ConversationViewModelEditTests`; note this test needs a temp `_vo/` dir and `ProjectPath`)

```csharp
    [Fact]
    public void NextNodeId_SkipsIdWithLeftoverVoFile()
    {
        var projectDir = Path.Combine(Path.GetTempPath(), $"NidTest_{Guid.NewGuid():N}");
        var voDir      = Path.Combine(projectDir, "_vo", "eder");
        Directory.CreateDirectory(voDir);
        try
        {
            var vm   = MakeVm();
            var node = new ConversationNode(1, false, SpeakerCategory.Npc, "", "", [],
                [], [], "Conversation", "None");
            vm.Load(new Conversation("conv", [node], StringTable.Empty));
            vm.ProjectPath = Path.Combine(projectDir, "p.dialogproject");

            // ID 2 would be next, but a leftover VO file reserves it.
            File.WriteAllText(Path.Combine(voDir, "conv_0002.wem"), "");

            Assert.Equal(3, vm.NextNodeId());
        }
        finally
        {
            try { Directory.Delete(projectDir, recursive: true); } catch (Exception) { /* best-effort */ }
        }
    }
```

- [ ] **Step 5: Run to verify failure** — expected: compile error (`NextNodeId` not found).

- [ ] **Step 6: Implement `NextNodeId` and rewire call sites**

In `ConversationViewModel`:

```csharp
    /// Next free node ID, skipping IDs that still own a _vo/ voice-over file —
    /// reusing such an ID would silently attach the deleted node's audio to the
    /// new node (file names are <conversation>_<id:0000>.wem).
    public int NextNodeId() =>
        NodeIdAllocator.Next(Nodes.Select(n => n.NodeId), IsNodeIdReservedByVo);

    private bool IsNodeIdReservedByVo(int id)
    {
        if (ProjectPath is null || string.IsNullOrEmpty(ConversationName)) return false;
        var voDir = Path.Combine(Path.GetDirectoryName(ProjectPath)!, "_vo");
        if (!Directory.Exists(voDir)) return false;
        var fileName = $"{ConversationName.ToLowerInvariant()}_{id:0000}.wem";
        try
        {
            return Directory.EnumerateFiles(voDir, fileName, SearchOption.AllDirectories).Any();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"VO reservation check failed for id {id}: {ex.Message}");
            return false;
        }
    }
```

Replace `NodeIdAllocator.Next(Nodes.Select(n => n.NodeId))` in `AddConnectedNode` with `NextNodeId()`, and both `NodeIdAllocator.Next(vm.Nodes.Select(n => n.NodeId))` occurrences in `ConversationView.axaml.cs` with `vm.NextNodeId()`.

- [ ] **Step 7: Run full suite** — `dotnet test DialogEditor.Tests --nologo`, expected PASS.

- [ ] **Step 8: Commit**

```bash
git add DialogEditor.Core/Editing/NodeIdAllocator.cs DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs "DialogEditor.Avalonia/Views/ConversationView.axaml.cs" DialogEditor.Tests
git commit -m "feat(vo): never reuse a node ID that still owns a _vo/ voice-over file"
```

---

### Task 5: Orphan section + armed cleanup in `VoValidationViewModel`

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/VoValidationViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/VoValidationViewModelTests.cs`

**Interfaces:**
- Consumes: delegate results shaped like `VoOrphanScanner.FindOrphans` output (full paths).
- Produces (Task 6 binds these):
  - `public record VoOrphanIssue(string FullPath, string RelativePath)`
  - `ObservableCollection<VoOrphanIssue> OrphanResults`
  - `bool HasOrphans`, `bool IsCleanUpArmed`, `string CleanUpConfirmText`
  - `public Func<CancellationToken, IReadOnlyList<string>>? OrphanScanner { get; set; }`
  - `public string? VoRootPath { get; set; }`
  - `IRelayCommand CleanUpCommand` (arms), `IRelayCommand ConfirmCleanUpCommand` (deletes), `IRelayCommand CancelCleanUpCommand` (disarms)

- [ ] **Step 1: Write the failing tests** (append to `VoValidationViewModelTests`)

```csharp
    // ── Orphan scan + cleanup ─────────────────────────────────────────────

    private (VoValidationViewModel Vm, string VoDir, string OrphanPath) MakeVmWithOrphan()
    {
        var voDir  = Path.Combine(_gameRoot, "proj", "_vo");
        var subDir = Path.Combine(voDir, "eder");
        Directory.CreateDirectory(subDir);
        var orphan = Path.Combine(subDir, "conv_0042.wem");
        File.WriteAllText(orphan, "");

        var vm = new VoValidationViewModel([], "test_conv", _gameRoot, "poe2")
        {
            VoRootPath    = voDir,
            OrphanScanner = _ => Directory.Exists(voDir)
                ? Directory.EnumerateFiles(voDir, "*.wem", SearchOption.AllDirectories).ToList()
                : [],
        };
        return (vm, voDir, orphan);
    }

    [Fact]
    public async Task RunAsync_OrphanScannerResults_PopulateOrphanResults()
    {
        var (vm, _, orphan) = MakeVmWithOrphan();

        await vm.RunAsync();

        var issue = Assert.Single(vm.OrphanResults);
        Assert.Equal(orphan, issue.FullPath);
        Assert.Equal(Path.Combine("eder", "conv_0042.wem"), issue.RelativePath);
        Assert.True(vm.HasOrphans);
    }

    [Fact]
    public async Task CleanUp_FirstClickArms_DoesNotDelete()
    {
        var (vm, _, orphan) = MakeVmWithOrphan();
        await vm.RunAsync();

        vm.CleanUpCommand.Execute(null);

        Assert.True(vm.IsCleanUpArmed);
        Assert.True(File.Exists(orphan));
    }

    [Fact]
    public async Task ConfirmCleanUp_DeletesOrphansAndPrunesEmptyDirs()
    {
        var (vm, voDir, orphan) = MakeVmWithOrphan();
        await vm.RunAsync();
        vm.CleanUpCommand.Execute(null);

        vm.ConfirmCleanUpCommand.Execute(null);

        Assert.False(File.Exists(orphan));
        Assert.False(Directory.Exists(Path.Combine(voDir, "eder")));  // pruned
        Assert.True(Directory.Exists(voDir));                          // root stays
        Assert.False(vm.IsCleanUpArmed);
        Assert.Empty(vm.OrphanResults);
    }

    [Fact]
    public async Task CancelCleanUp_DisarmsWithoutDeleting()
    {
        var (vm, _, orphan) = MakeVmWithOrphan();
        await vm.RunAsync();
        vm.CleanUpCommand.Execute(null);

        vm.CancelCleanUpCommand.Execute(null);

        Assert.False(vm.IsCleanUpArmed);
        Assert.True(File.Exists(orphan));
    }
```

- [ ] **Step 2: Run to verify failure** — expected: compile errors (`VoRootPath`, `OrphanScanner`, `OrphanResults` … not found).

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~VoValidationViewModelTests" --nologo`

- [ ] **Step 3: Implement**

Add to `VoValidationViewModel.cs`:

```csharp
/// A .wem in _vo/ that no VO-enabled node references (candidate for cleanup).
public record VoOrphanIssue(string FullPath, string RelativePath);
```

and inside the class:

```csharp
    /// Project-wide orphan scan, injected by MainWindowViewModel (wraps
    /// VoOrphanScanner.FindOrphans with the live project/provider/canvas).
    /// Null (e.g. no project open) disables the orphan section.
    public Func<CancellationToken, IReadOnlyList<string>>? OrphanScanner { get; set; }

    /// The _vo/ folder; used to compute display-relative paths and prune
    /// empty prefix directories after cleanup.
    public string? VoRootPath { get; set; }

    public ObservableCollection<VoOrphanIssue> OrphanResults { get; } = [];
    public bool HasOrphans => OrphanResults.Count > 0;

    [ObservableProperty] private bool _isCleanUpArmed;

    /// Localised "Delete {0} file(s)…" confirmation line for the armed state.
    public string CleanUpConfirmText => Loc.Format("VoValidation_CleanUpConfirm", OrphanResults.Count);

    public IRelayCommand CleanUpCommand        { get; }
    public IRelayCommand ConfirmCleanUpCommand { get; }
    public IRelayCommand CancelCleanUpCommand  { get; }
```

Constructor additions (after the existing command setup):

```csharp
        CleanUpCommand        = new RelayCommand(() => IsCleanUpArmed = true,  () => HasOrphans && !IsCleanUpArmed);
        ConfirmCleanUpCommand = new RelayCommand(ExecuteCleanUp,               () => IsCleanUpArmed);
        CancelCleanUpCommand  = new RelayCommand(() => IsCleanUpArmed = false, () => IsCleanUpArmed);

        OrphanResults.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasOrphans));
            OnPropertyChanged(nameof(CleanUpConfirmText));
            CleanUpCommand.NotifyCanExecuteChanged();
        };
```

plus:

```csharp
    partial void OnIsCleanUpArmedChanged(bool value)
    {
        CleanUpCommand.NotifyCanExecuteChanged();
        ConfirmCleanUpCommand.NotifyCanExecuteChanged();
        CancelCleanUpCommand.NotifyCanExecuteChanged();
    }

    private void ExecuteCleanUp()
    {
        var files  = OrphanResults.Select(o => o.FullPath).ToList();
        var failed = 0;
        foreach (var f in files)
        {
            try { File.Delete(f); }
            catch (Exception ex) { failed++; AppLog.Warn($"VO cleanup: could not delete '{f}': {ex.Message}"); }
        }
        PruneEmptyVoDirectories();
        IsCleanUpArmed = false;
        OrphanResults.Clear();
        SummaryText = failed == 0
            ? Loc.Format("VoValidation_CleanedUp", files.Count)
            : Loc.Format("VoValidation_CleanUpPartial", files.Count - failed, failed);
        _ = RunAsync();   // refresh both sections against reality
    }

    private void PruneEmptyVoDirectories()
    {
        if (VoRootPath is null || !Directory.Exists(VoRootPath)) return;
        foreach (var dir in Directory.GetDirectories(VoRootPath, "*", SearchOption.AllDirectories)
                                     .OrderByDescending(d => d.Length))   // deepest first
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch (Exception ex) { AppLog.Warn($"VO cleanup: could not remove empty dir '{dir}': {ex.Message}"); }
        }
    }
```

In `RunAsync`, inside the `Task.Run` body after the node loop, run the orphan scan; collect into a local list applied in `finally` (same batching pattern as `batch`):

```csharp
                var orphanBatch = new List<VoOrphanIssue>();   // declared next to `batch`
                // ... existing node loop ...
                if (OrphanScanner is not null)
                {
                    token.ThrowIfCancellationRequested();
                    foreach (var path in OrphanScanner(token))
                        orphanBatch.Add(new VoOrphanIssue(path,
                            VoRootPath is not null ? Path.GetRelativePath(VoRootPath, path)
                                                   : Path.GetFileName(path)));
                }
```

and in `finally`, before setting `IsRunning = false`:

```csharp
            OrphanResults.Clear();
            foreach (var issue in orphanBatch)
                OrphanResults.Add(issue);
            IsCleanUpArmed = false;
```

(`Results.Clear()` at the top of `RunAsync` already resets the missing section; clear `OrphanResults` in `finally` only, so a cancelled scan keeps prior results consistent with the existing pattern.)

- [ ] **Step 4: Run the test class** — expected PASS.

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~VoValidationViewModelTests" --nologo`

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/VoValidationViewModel.cs DialogEditor.Tests/ViewModels/VoValidationViewModelTests.cs
git commit -m "feat(vo): orphan results and armed two-click cleanup in VO validation"
```

---

### Task 6: Window UI, strings, and MainWindowViewModel wiring

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Modify: `DialogEditor.Avalonia/Views/VoValidationWindow.axaml`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (`CreateVoValidationViewModel`, ~line 401)
- Test: covered by Task 5 VM tests + full suite; UI verified manually after the plan completes.

**Interfaces:**
- Consumes: everything Task 5 produced.

- [ ] **Step 1: Add strings** (into the Validate Voice-Over block of `Strings.axaml`; keep neighbors' comment style)

```xml
    <!-- ── VO validation — orphaned files section ────────────────────────── -->
    <sys:String x:Key="VoValidation_OrphanSection">Orphaned voice-over files</sys:String>
    <sys:String x:Key="VoValidation_OrphanBadge">ORPHAN</sys:String>
    <sys:String x:Key="Button_CleanUp">Clean up…</sys:String>
    <sys:String x:Key="ToolTip_CleanUp">Delete the orphaned .wem files listed above from the project's _vo folder. They belong to deleted nodes, removed conversations, or female slots whose node no longer has female text. You will be asked to confirm; game files are never touched.</sys:String>
    <!-- {0} = number of files -->
    <sys:String x:Key="VoValidation_CleanUpConfirm">Delete {0} orphaned file(s) from the _vo folder? The editor cannot undo this — re-import the source audio to restore a file.</sys:String>
    <sys:String x:Key="Button_ConfirmCleanUp">Delete files</sys:String>
    <sys:String x:Key="ToolTip_ConfirmCleanUp">Permanently delete the listed orphaned files from the _vo folder.</sys:String>
    <sys:String x:Key="Button_CancelCleanUp">Keep files</sys:String>
    <sys:String x:Key="ToolTip_CancelCleanUp">Close the confirmation without deleting anything.</sys:String>
    <!-- {0} = files deleted -->
    <sys:String x:Key="VoValidation_CleanedUp">Deleted {0} orphaned file(s).</sys:String>
    <!-- {0} = files deleted, {1} = failures -->
    <sys:String x:Key="VoValidation_CleanUpPartial">Deleted {0} file(s); {1} could not be deleted — see the log.</sys:String>
```

- [ ] **Step 2: Extend `VoValidationWindow.axaml`**

Change the root grid to `RowDefinitions="Auto,Auto,Auto,*,Auto,Auto"` and insert the orphan section as row 4 (moving `FocusHintBar` to row 5). New section, below the existing results `ScrollViewer`:

```xml
        <!-- Orphaned _vo/ files + armed cleanup -->
        <StackPanel Grid.Row="4" IsVisible="{Binding HasOrphans}" Margin="0,0,0,6">
            <TextBlock Text="{DynamicResource VoValidation_OrphanSection}"
                       FontWeight="SemiBold"
                       Foreground="{DynamicResource Brush.Text.Primary}"
                       Margin="0,4,0,4"/>
            <ScrollViewer MaxHeight="120">
                <ItemsControl ItemsSource="{Binding OrphanResults}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="vm:VoOrphanIssue">
                            <Grid ColumnDefinitions="*,Auto" Margin="0,2">
                                <TextBlock Grid.Column="0" Text="{Binding RelativePath}"
                                           Foreground="{DynamicResource Brush.Text.Secondary}"
                                           FontSize="{DynamicResource FontSize.Label}"
                                           TextTrimming="CharacterEllipsis"
                                           VerticalAlignment="Center"/>
                                <TextBlock Grid.Column="1"
                                           Text="{DynamicResource VoValidation_OrphanBadge}"
                                           Foreground="{DynamicResource Brush.Severity.Warning}"
                                           FontSize="{DynamicResource FontSize.Small}"
                                           FontWeight="Bold"
                                           VerticalAlignment="Center" Margin="8,0,0,0"/>
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>

            <Button Content="{DynamicResource Button_CleanUp}"
                    Command="{Binding CleanUpCommand}"
                    IsVisible="{Binding IsCleanUpArmed, Converter={StaticResource InverseBoolToVis}}"
                    ToolTip.Tip="{DynamicResource ToolTip_CleanUp}"
                    AutomationProperties.HelpText="{DynamicResource ToolTip_CleanUp}"
                    Margin="0,6,0,0"/>

            <StackPanel Orientation="Vertical" IsVisible="{Binding IsCleanUpArmed}" Margin="0,6,0,0" Spacing="6">
                <TextBlock Text="{Binding CleanUpConfirmText}"
                           Foreground="{DynamicResource Brush.Severity.Warning}"
                           TextWrapping="Wrap"/>
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Button Content="{DynamicResource Button_ConfirmCleanUp}"
                            Command="{Binding ConfirmCleanUpCommand}"
                            ToolTip.Tip="{DynamicResource ToolTip_ConfirmCleanUp}"
                            AutomationProperties.HelpText="{DynamicResource ToolTip_ConfirmCleanUp}"/>
                    <Button Content="{DynamicResource Button_CancelCleanUp}"
                            Command="{Binding CancelCleanUpCommand}"
                            ToolTip.Tip="{DynamicResource ToolTip_CancelCleanUp}"
                            AutomationProperties.HelpText="{DynamicResource ToolTip_CancelCleanUp}"/>
                </StackPanel>
            </StackPanel>
        </StackPanel>

        <!-- Focus hint bar -->
        <shared:FocusHintBar Grid.Row="5" x:Name="HintBar"/>
```

(Remove the old `Grid.Row="4"` from the original `FocusHintBar` element — it is replaced by the row-5 version above.)

- [ ] **Step 3: Wire the scanner in `MainWindowViewModel.CreateVoValidationViewModel`**

```csharp
    public VoValidationViewModel? CreateVoValidationViewModel()
    {
        if (!CanValidateVO) return null;
        var snapshot = Canvas.BuildSnapshot();
        var vm = new VoValidationViewModel(
            snapshot.Nodes, Canvas.ConversationName,
            _currentGameDirectory, _activeGameId, _projectPath);

        // Orphan section only makes sense with a saved project (the _vo/ folder
        // lives next to the project file).
        if (_project is not null && _provider is not null && _projectPath is not null)
        {
            var project     = _project;
            var provider    = _provider;
            var projectPath = _projectPath;
            var convName    = Canvas.ConversationName;
            vm.VoRootPath    = Path.Combine(Path.GetDirectoryName(projectPath)!, "_vo");
            vm.OrphanScanner = _ => VoOrphanScanner.FindOrphans(
                project, provider, projectPath, convName, snapshot);
        }
        return vm;
    }
```

- [ ] **Step 4: Run the full suite**

Run: `dotnet test DialogEditor.Tests --nologo`
Expected: PASS, zero failures.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Avalonia/Views/VoValidationWindow.axaml DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git commit -m "feat(vo): orphaned-files section with confirmed cleanup in Validate Voice-Over"
```

---

### Task 7: Final verification

- [ ] **Step 1: Full suite** — `dotnet test DialogEditor.Tests --nologo`, expected: PASS, zero failures, no new warnings beyond the pre-existing CS8620 pair in `LookupKindWhitelistTests`.
- [ ] **Step 2: Manual smoke check with the user's real project** (only if the editor is closed): the fem indication on node 107 of `21_si_pallid_knight` should now be gone (no female text), and Validate Voice-Over should list `global_god_berath/21_si_pallid_knight_0107_fem.wem` as an orphan.
- [ ] **Step 3: Update `Gaps.md`** if it lists VO-lifecycle items now covered; do **not** touch `CHANGELOG.md` (frozen pre-release).
- [ ] **Step 4: Commit** any doc updates:

```bash
git add Gaps.md
git commit -m "docs(gaps): VO lifecycle cleanup implemented"
```
