# Stale Patch Data Hygiene Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect and prune stale `NodeComments`/`Translations`/`Layouts` entries that reference deleted node IDs, and stop the leak at the save-time source.

**Architecture:** A pure `ProjectStaleDataScanner` classifies referenced node IDs as *confirmed* stale (ID ∈ the patch's `DeletedNodeIds`, always detected) or *likely* stale (ID absent from the reconstructed effective node set, only via an injected delegate). A pure `StaleDataPruner` rebuilds cleaned patches/layouts. Save-time filtering in `FoldCanvasIntoProject` prevents new rot. The report + armed prune surface as a new "Stale data" section in the existing Validate Text window.

**Tech Stack:** C#/.NET, Avalonia, CommunityToolkit.Mvvm, xUnit. Records + `with`-expressions on immutable `DialogProject`/`ConversationPatch`.

## Global Constraints

- **TDD (red/green/refactor):** every non-trivial behaviour gets a failing test first. Tests mirror `DialogEditor.Core`/`.Patch`/`.ViewModels` structure under `DialogEditor.Tests`.
- **Localisation:** no user-visible string hard-coded in XAML or C#. All new strings go in `DialogEditor.Avalonia/Resources/Strings.axaml` and are referenced via `{DynamicResource}` (XAML) or `Loc.Get`/`Loc.Format`/`Loc.FormatCount` (C#).
- **Tooltips + automation:** every new interactive control carries a detailed `ToolTip.Tip` and a paired `AutomationProperties.HelpText` (mirroring rule) / `AutomationProperties.Name` where the content is glyph-only. Enforced by `AutomationHelpTextTests`/`AutomationNameTests`.
- **Error handling:** every caught exception in production code logs via `AppLog.Error`/`AppLog.Warn` except `OperationCanceledException` (swallowed). No bare `catch {}` in production. Test-teardown cleanup may swallow.
- **Tests run serially** (AppSettings/Loc global-state race) — do not add parallelism; VM/label tests must call `Loc.Configure(new StubStringProvider())` in their constructor.
- **CHANGELOG.md is frozen** — do not touch it.

---

### Task 1: `ProjectStaleDataScanner` — types + confirmed pass (pure)

**Files:**
- Create: `DialogEditor.ViewModels/Services/ProjectStaleDataScanner.cs`
- Test: `DialogEditor.Tests/Services/ProjectStaleDataScannerTests.cs`

**Interfaces:**
- Consumes: `DialogEditor.Patch.DialogProject`, `ConversationPatch`, `NodeTranslation`, `LayoutPoint`.
- Produces:
  - `enum StaleDataKind { Comment, Translation, Layout }`
  - `enum StaleConfidence { Confirmed, Likely }`
  - `record StaleDataRow(string ConversationName, int NodeId, StaleDataKind Kind, string? Language, StaleConfidence Confidence)`
  - `static IReadOnlyList<StaleDataRow> ProjectStaleDataScanner.Scan(DialogProject project, Func<string, IReadOnlySet<int>?>? effectiveNodeIds = null)`

- [ ] **Step 1: Write the failing test**

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class ProjectStaleDataScannerTests
{
    private static ConversationPatch Patch(
        string name,
        IReadOnlyList<int> deleted,
        IReadOnlyDictionary<int, string>? comments = null,
        IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>>? translations = null)
        => new(name, ConversationPatch.CurrentSchemaVersion, [], deleted, [])
           {
               NodeComments = comments ?? new Dictionary<int, string>(),
               Translations = translations ?? new Dictionary<string, IReadOnlyList<NodeTranslation>>(),
           };

    private static DialogProject Project(params ConversationPatch[] patches)
    {
        var project = DialogProject.Empty("Test");
        foreach (var p in patches) project = project.WithPatch(p);
        return project;
    }

    [Fact]
    public void Comment_ForDeletedNode_ReportedConfirmed()
    {
        var project = Project(Patch("conv_a", deleted: [7],
            comments: new Dictionary<int, string> { [7] = "old note", [3] = "live note" }));

        var row = Assert.Single(ProjectStaleDataScanner.Scan(project));
        Assert.Equal(("conv_a", 7, StaleDataKind.Comment, (string?)null, StaleConfidence.Confirmed),
            (row.ConversationName, row.NodeId, row.Kind, row.Language, row.Confidence));
    }

    [Fact]
    public void Translation_ForDeletedNode_ReportedPerLanguage()
    {
        var project = Project(Patch("conv_a", deleted: [9],
            translations: new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(9, "gone", ""), new NodeTranslation(2, "stays", "")],
                ["de"] = [new NodeTranslation(9, "weg", "")],
            }));

        var rows = ProjectStaleDataScanner.Scan(project);
        Assert.Equal(
            [("conv_a", 9, "en"), ("conv_a", 9, "de")],
            rows.Select(r => (r.ConversationName, r.NodeId, r.Language)).ToList());
        Assert.All(rows, r => Assert.Equal(StaleConfidence.Confirmed, r.Confidence));
    }

    [Fact]
    public void Layout_ForDeletedNode_ReportedConfirmed()
    {
        var project = Project(Patch("conv_a", deleted: [4]))
            .WithLayout("conv_a", new Dictionary<int, LayoutPoint>
            {
                [4] = new LayoutPoint(10, 10), [1] = new LayoutPoint(20, 20),
            });

        var row = Assert.Single(ProjectStaleDataScanner.Scan(project));
        Assert.Equal((4, StaleDataKind.Layout), (row.NodeId, row.Kind));
    }

    [Fact]
    public void CleanProject_Empty()
    {
        var project = Project(Patch("conv_a", deleted: [],
            comments: new Dictionary<int, string> { [1] = "note" },
            translations: new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(1, "hi", "")],
            }));
        Assert.Empty(ProjectStaleDataScanner.Scan(project));
    }

    [Fact]
    public void NoDelegate_ProducesOnlyConfirmedRows()
    {
        var project = Project(Patch("conv_a", deleted: [7],
            comments: new Dictionary<int, string> { [7] = "x", [8] = "y" }));
        // 8 is not deleted and there is no effective-set delegate, so 8 is not flagged.
        var row = Assert.Single(ProjectStaleDataScanner.Scan(project));
        Assert.Equal(7, row.NodeId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~ProjectStaleDataScannerTests`
Expected: FAIL — `ProjectStaleDataScanner` / `StaleDataRow` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.ViewModels.Services;

/// Which node-keyed data category a stale reference lives in.
public enum StaleDataKind { Comment, Translation, Layout }

/// Confirmed = referenced ID is in the patch's own DeletedNodeIds (zero false
/// positives). Likely = ID absent from the reconstructed effective node set
/// (may be a version-skew false positive).
public enum StaleConfidence { Confirmed, Likely }

/// One stale-data finding. Language is the raw code (e.g. "en"/"de") for
/// Translation rows, null for Comment/Layout. Display labelling is the VM's job
/// — this record is Loc-free so the scanner stays pure and testable.
public sealed record StaleDataRow(
    string ConversationName, int NodeId, StaleDataKind Kind,
    string? Language, StaleConfidence Confidence);

/// Finds NodeComments/Translations/Layouts entries pointing at nodes that no
/// longer exist. The confirmed pass is pure over project.Patches. The likely
/// pass runs only when effectiveNodeIds is supplied: it maps a conversation
/// name to its live node-ID set, or null when the conversation can't be
/// resolved (that conversation is then skipped, never flagged).
/// Spec: docs/superpowers/specs/2026-07-13-stale-patch-data-hygiene-design.md
public static class ProjectStaleDataScanner
{
    public static IReadOnlyList<StaleDataRow> Scan(
        DialogProject project,
        Func<string, IReadOnlySet<int>?>? effectiveNodeIds = null)
    {
        var rows = new List<StaleDataRow>();

        foreach (var (conv, patch) in project.Patches)
        {
            var deleted   = patch.DeletedNodeIds.ToHashSet();
            var effective = effectiveNodeIds?.Invoke(conv);

            foreach (var (kind, id, lang) in References(conv, patch, project))
            {
                if (deleted.Contains(id))
                    rows.Add(new StaleDataRow(conv, id, kind, lang, StaleConfidence.Confirmed));
                else if (effective is not null && !effective.Contains(id))
                    rows.Add(new StaleDataRow(conv, id, kind, lang, StaleConfidence.Likely));
            }
        }

        return rows
            .OrderBy(r => r.ConversationName, StringComparer.Ordinal)
            .ThenBy(r => r.NodeId)
            .ThenBy(r => r.Kind)
            .ThenBy(r => r.Language, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<(StaleDataKind Kind, int NodeId, string? Lang)> References(
        string conv, ConversationPatch patch, DialogProject project)
    {
        foreach (var id in patch.NodeComments.Keys)
            yield return (StaleDataKind.Comment, id, null);

        foreach (var (lang, entries) in patch.Translations)
            foreach (var t in entries)
                yield return (StaleDataKind.Translation, t.NodeId, lang);

        var layout = project.GetLayout(conv);
        if (layout is not null)
            foreach (var id in layout.Keys)
                yield return (StaleDataKind.Layout, id, null);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~ProjectStaleDataScannerTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/ProjectStaleDataScanner.cs DialogEditor.Tests/Services/ProjectStaleDataScannerTests.cs
git commit -m "feat(hygiene): stale-data scanner confirmed pass"
```

---

### Task 2: `ProjectStaleDataScanner` — likely pass (effective-set delegate)

**Files:**
- Modify: `DialogEditor.Tests/Services/ProjectStaleDataScannerTests.cs` (add tests only — the implementation from Task 1 already handles the delegate; these tests lock the behaviour)

**Interfaces:**
- Consumes: `ProjectStaleDataScanner.Scan(project, effectiveNodeIds)` from Task 1.
- Produces: nothing new.

- [ ] **Step 1: Write the failing tests**

Add to `ProjectStaleDataScannerTests`:

```csharp
    // A stub effective-set delegate: returns the given set for the named
    // conversation, null (unresolvable → skip) for any other name.
    private static Func<string, IReadOnlySet<int>?> Effective(
        string conv, params int[] liveIds)
        => name => name == conv ? new HashSet<int>(liveIds) : null;

    [Fact]
    public void Likely_WhenIdAbsentFromEffectiveSet_AndNotDeleted()
    {
        // Node 8 was an added node the writer later deleted: not in DeletedNodeIds,
        // not in the effective set — a "likely" orphan.
        var project = Project(Patch("conv_a", deleted: [],
            comments: new Dictionary<int, string> { [8] = "orphan", [3] = "live" }));

        var rows = ProjectStaleDataScanner.Scan(project, Effective("conv_a", 3));
        var row = Assert.Single(rows);
        Assert.Equal((8, StaleConfidence.Likely), (row.NodeId, row.Confidence));
    }

    [Fact]
    public void VanillaNodeComment_NotFlagged_WhenPresentInEffectiveSet()
    {
        // Node 3 is an unmodified vanilla node carrying a translator note: it is not
        // in any structural set but IS in the effective set, so it must NOT be flagged.
        var project = Project(Patch("conv_a", deleted: [],
            comments: new Dictionary<int, string> { [3] = "note on a vanilla line" }));

        Assert.Empty(ProjectStaleDataScanner.Scan(project, Effective("conv_a", 3)));
    }

    [Fact]
    public void UnresolvableConversation_Skipped_NotFlagged()
    {
        // Delegate returns null for conv_a → we cannot judge staleness → nothing flagged.
        var project = Project(Patch("conv_a", deleted: [],
            comments: new Dictionary<int, string> { [8] = "orphan" }));

        Assert.Empty(ProjectStaleDataScanner.Scan(project, name => null));
    }

    [Fact]
    public void DeletedNode_ReportedConfirmed_NotDoubleReportedAsLikely()
    {
        // 7 is deleted (confirmed) and also absent from the effective set; must appear once.
        var project = Project(Patch("conv_a", deleted: [7],
            comments: new Dictionary<int, string> { [7] = "gone" }));

        var row = Assert.Single(ProjectStaleDataScanner.Scan(project, Effective("conv_a", 3)));
        Assert.Equal(StaleConfidence.Confirmed, row.Confidence);
    }
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~ProjectStaleDataScannerTests`
Expected: PASS (9 tests). The Task-1 implementation already satisfies these; if any fails, fix `Scan` (the `else if (effective is not null && !effective.Contains(id))` branch and the confirmed-first ordering) rather than the test.

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Tests/Services/ProjectStaleDataScannerTests.cs
git commit -m "test(hygiene): lock likely-stale detection + skip/no-double-report"
```

---

### Task 3: `StaleDataPruner` (pure)

**Files:**
- Create: `DialogEditor.ViewModels/Services/StaleDataPruner.cs`
- Test: `DialogEditor.Tests/Services/StaleDataPrunerTests.cs`

**Interfaces:**
- Consumes: `StaleDataRow`, `StaleDataKind` (Task 1); `DialogProject`, `ConversationPatch`, `NodeTranslation`, `LayoutPoint`.
- Produces: `static DialogProject StaleDataPruner.Prune(DialogProject project, IReadOnlyList<StaleDataRow> rows)`

- [ ] **Step 1: Write the failing test**

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class StaleDataPrunerTests
{
    private static DialogProject BuildProject()
    {
        var patch = new ConversationPatch("conv_a", ConversationPatch.CurrentSchemaVersion, [], [7], [])
        {
            NodeComments = new Dictionary<int, string> { [7] = "gone", [3] = "live" },
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(7, "gone", ""), new NodeTranslation(3, "stays", "")],
                ["de"] = [new NodeTranslation(7, "weg", "")],
            },
        };
        return DialogProject.Empty("Test").WithPatch(patch)
            .WithLayout("conv_a", new Dictionary<int, LayoutPoint>
            {
                [7] = new LayoutPoint(1, 1), [3] = new LayoutPoint(2, 2),
            });
    }

    [Fact]
    public void Prune_RemovesComment_Translation_AndLayout_ForNode()
    {
        var project = BuildProject();
        var rows = new List<StaleDataRow>
        {
            new("conv_a", 7, StaleDataKind.Comment,     null, StaleConfidence.Confirmed),
            new("conv_a", 7, StaleDataKind.Translation, "en", StaleConfidence.Confirmed),
            new("conv_a", 7, StaleDataKind.Translation, "de", StaleConfidence.Confirmed),
            new("conv_a", 7, StaleDataKind.Layout,      null, StaleConfidence.Confirmed),
        };

        var result = StaleDataPruner.Prune(project, rows);
        var patch  = result.Patches["conv_a"];

        Assert.False(patch.NodeComments.ContainsKey(7));
        Assert.True(patch.NodeComments.ContainsKey(3));
        Assert.DoesNotContain(patch.Translations["en"], t => t.NodeId == 7);
        Assert.Contains(patch.Translations["en"], t => t.NodeId == 3);
        Assert.False(patch.Translations.ContainsKey("de")); // last entry removed → language dropped
        Assert.False(result.GetLayout("conv_a")!.ContainsKey(7));
        Assert.True(result.GetLayout("conv_a")!.ContainsKey(3));
    }

    [Fact]
    public void Prune_OnlyRemovesTranslation_ForNamedLanguage()
    {
        var project = BuildProject();
        // Only the German translation of node 7 is pruned; English row 7 stays.
        var rows = new List<StaleDataRow>
        {
            new("conv_a", 7, StaleDataKind.Translation, "de", StaleConfidence.Likely),
        };

        var patch = StaleDataPruner.Prune(project, rows).Patches["conv_a"];
        Assert.False(patch.Translations.ContainsKey("de"));
        Assert.Contains(patch.Translations["en"], t => t.NodeId == 7);
        Assert.True(patch.NodeComments.ContainsKey(7)); // comment untouched
    }

    [Fact]
    public void Prune_EmptyRows_ReturnsProjectUnchanged()
    {
        var project = BuildProject();
        var result  = StaleDataPruner.Prune(project, []);
        Assert.True(result.Patches["conv_a"].NodeComments.ContainsKey(7));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~StaleDataPrunerTests`
Expected: FAIL — `StaleDataPruner` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.ViewModels.Services;

/// Rebuilds a DialogProject with the given stale rows removed. Comments and
/// translations live on the ConversationPatch; layout lives on the project.
/// Pure: returns a new immutable project, mutates nothing.
public static class StaleDataPruner
{
    public static DialogProject Prune(DialogProject project, IReadOnlyList<StaleDataRow> rows)
    {
        var result = project;

        foreach (var group in rows.GroupBy(r => r.ConversationName))
        {
            var conv = group.Key;

            if (result.Patches.TryGetValue(conv, out var patch))
            {
                var commentIds = group.Where(r => r.Kind == StaleDataKind.Comment)
                                      .Select(r => r.NodeId).ToHashSet();
                var transKeys  = group.Where(r => r.Kind == StaleDataKind.Translation)
                                      .Select(r => (r.NodeId, r.Language)).ToHashSet();

                var newComments = commentIds.Count == 0
                    ? patch.NodeComments
                    : patch.NodeComments.Where(kv => !commentIds.Contains(kv.Key))
                                        .ToDictionary(kv => kv.Key, kv => kv.Value);

                var newTranslations = new Dictionary<string, IReadOnlyList<NodeTranslation>>();
                foreach (var (lang, entries) in patch.Translations)
                {
                    var kept = entries.Where(t => !transKeys.Contains((t.NodeId, lang))).ToList();
                    if (kept.Count > 0) newTranslations[lang] = kept;
                }

                result = result.WithPatch(patch with
                {
                    NodeComments = newComments,
                    Translations = newTranslations,
                });
            }

            var layoutIds = group.Where(r => r.Kind == StaleDataKind.Layout)
                                 .Select(r => r.NodeId).ToHashSet();
            if (layoutIds.Count > 0 && result.GetLayout(conv) is { } layout)
            {
                result = result.WithLayout(conv,
                    layout.Where(kv => !layoutIds.Contains(kv.Key))
                          .ToDictionary(kv => kv.Key, kv => kv.Value));
            }
        }

        return result;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~StaleDataPrunerTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/StaleDataPruner.cs DialogEditor.Tests/Services/StaleDataPrunerTests.cs
git commit -m "feat(hygiene): pure StaleDataPruner rebuilds cleaned project"
```

---

### Task 4: Save-time prevention in `FoldCanvasIntoProject`

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs:1315-1341` (`FoldCanvasIntoProject`)
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelPersistenceTests.cs` (add a fact)

**Interfaces:**
- Consumes: `Canvas.BuildSnapshot()` (existing), `ConversationPatch` `with`-expression.
- Produces: no new public surface — a saved patch never contains comment/translation entries for node IDs absent from the canvas.

- [ ] **Step 1: Write the failing test**

First inspect the existing persistence-test helpers to reuse the project-open/save harness:

Run: `sed -n '1,60p' DialogEditor.Tests/ViewModels/MainWindowViewModelPersistenceTests.cs`

Then add a fact modelled on the existing open→edit→save fixtures in that file (reuse its helper for opening a project and a conversation with a base snapshot). The behaviour to assert:

```csharp
[Fact]
public void Save_DropsCommentAndTranslations_ForDeletedNode()
{
    // Arrange: open a conversation with nodes {1,2}; node 2 carries a comment and
    // a non-canvas-language (de) translation. (Use this file's existing open helper.)
    var vm = OpenProjectWithConversation(/* nodes */ 1, 2);
    vm.Canvas.SetNodeComment(2, "note that should die with the node");
    SeedForeignTranslation(vm, conv: vm.Canvas.ConversationName, lang: "de", nodeId: 2, text: "weg");

    // Act: delete node 2 on the canvas, then save.
    vm.Canvas.DeleteNode(vm.Canvas.Nodes.Single(n => n.NodeId == 2));
    vm.SaveProject();

    // Assert: the saved patch has no comment or translation referencing node 2.
    var patch = vm.ProjectForTest.Patches[vm.Canvas.ConversationName];
    Assert.False(patch.NodeComments.ContainsKey(2));
    Assert.DoesNotContain(
        patch.Translations.SelectMany(kv => kv.Value), t => t.NodeId == 2);
}
```

> If `OpenProjectWithConversation`, `SeedForeignTranslation`, or `ProjectForTest` don't already exist in this test file, add the smallest equivalent helpers alongside the existing ones (follow the file's current pattern for constructing a `MainWindowViewModel` with a stub `IGameDataProvider` and a base snapshot). `ProjectForTest` can expose `_project` via an existing internal accessor if one is present; otherwise assert against the re-read saved file the file's other tests use.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~MainWindowViewModelPersistenceTests.Save_DropsCommentAndTranslations_ForDeletedNode`
Expected: FAIL — node 2's comment and `de` translation survive the save.

- [ ] **Step 3: Write minimal implementation**

In `FoldCanvasIntoProject`, capture the snapshot once and filter after the translation-merge block:

```csharp
private void FoldCanvasIntoProject()
{
    if (_currentFile is null || Canvas.BaseSnapshot is null) return;

    var current = Canvas.BuildSnapshot();
    var patch   = DiffEngine.Diff(_currentFile.Name, Canvas.BaseSnapshot, current, _provider!.Language);
    patch = patch with { NodeComments = Canvas.NodeComments };

    if (_project!.Patches.TryGetValue(_currentFile.Name, out var prior)
        && prior.Translations.Count > 0)
    {
        var mergedTranslations =
            new Dictionary<string, IReadOnlyList<NodeTranslation>>(prior.Translations);
        mergedTranslations.Remove(_provider.Language);
        foreach (var (lang, entries) in patch.Translations)
            mergedTranslations[lang] = entries;
        patch = patch with { Translations = mergedTranslations };
    }

    // Prevention: the canvas holds the full effective conversation, so its live
    // node-ID set is authoritative (game-folder-free). Drop comment/translation
    // entries for nodes that no longer exist, so deleting a node cannot leave
    // stale patch data behind (see the Stale Patch Data Hygiene spec).
    var liveIds = current.Nodes.Select(n => n.NodeId).ToHashSet();
    patch = patch with
    {
        NodeComments = patch.NodeComments
            .Where(kv => liveIds.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value),
        Translations = patch.Translations.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<NodeTranslation>)
                  kv.Value.Where(t => liveIds.Contains(t.NodeId)).ToList()),
    };

    var layout      = Canvas.GetCurrentLayout();
    var annotations = Canvas.GetCurrentAnnotations();
    SetProject(_project!.WithPatch(patch).WithLayout(_currentFile.Name, layout)
        .WithAnnotations(_currentFile.Name, annotations));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~MainWindowViewModelPersistenceTests`
Expected: PASS (new fact + all existing persistence facts still green).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelPersistenceTests.cs
git commit -m "fix(hygiene): prune stale comments/translations at save time"
```

---

### Task 5: `TextTagValidationViewModel` — stale section + row VM + prune commands

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/StaleDataRowViewModel.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/TextTagValidationViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/TextTagValidationViewModelStaleTests.cs`

**Interfaces:**
- Consumes: `StaleDataRow`, `StaleConfidence`, `StaleDataKind` (Task 1); `Loc` (`DialogEditor.ViewModels.Resources`).
- Produces:
  - `StaleDataRowViewModel(StaleDataRow row, string primaryLanguage, Action<StaleDataRow>? removeOne)` with `ConversationName`, `NodeLabel`, `CategoryLabel`, `ConfidenceLabel`, `IsLikely`, `CanRemove`, `StaleDataRow Row`, `RemoveCommand`.
  - `TextTagValidationViewModel` extended constructor params (all optional so existing callers/tests compile):
    `Func<bool, IReadOnlyList<StaleDataRow>>? staleScan = null`, `Action<IReadOnlyList<StaleDataRow>>? prune = null`, `bool canCheckGameFiles = false`, `string primaryLanguage = ""`.
  - New public members on the VM: `ObservableCollection<StaleDataRowViewModel> StaleRows`, `bool HasStaleData`, `string StaleSummaryText`, `bool CanCheckGameFiles`, `bool CheckGameFiles`, `bool IsStaleCleanUpArmed`, `string StaleCleanUpConfirmText`, commands `CleanUpStaleCommand`/`ConfirmCleanUpStaleCommand`/`CancelCleanUpStaleCommand`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class TextTagValidationViewModelStaleTests
{
    public TextTagValidationViewModelStaleTests() => Loc.Configure(new StubStringProvider());

    private static StaleDataRow Confirmed(int id, StaleDataKind kind = StaleDataKind.Comment, string? lang = null)
        => new("conv_a", id, kind, lang, StaleConfidence.Confirmed);
    private static StaleDataRow Likely(int id)
        => new("conv_a", id, StaleDataKind.Comment, null, StaleConfidence.Likely);

    [Fact]
    public void StaleRows_PopulateFromScan()
    {
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            staleScan: _ => [Confirmed(7), Likely(8)],
            canCheckGameFiles: true);

        Assert.Equal(2, vm.StaleRows.Count);
        Assert.True(vm.HasStaleData);
    }

    [Fact]
    public void CleanUpStale_PrunesConfirmedRowsOnly()
    {
        IReadOnlyList<StaleDataRow>? pruned = null;
        var scanQueue = new Queue<IReadOnlyList<StaleDataRow>>(
        [
            [Confirmed(7), Likely(8)],  // initial
            [Likely(8)],                // after prune re-scan
        ]);

        var vm = new TextTagValidationViewModel(
            scan: () => [],
            staleScan: _ => scanQueue.Dequeue(),
            prune: rows => pruned = rows,
            canCheckGameFiles: true);

        vm.CleanUpStaleCommand.Execute(null);       // arm
        vm.ConfirmCleanUpStaleCommand.Execute(null); // confirm

        Assert.NotNull(pruned);
        Assert.All(pruned!, r => Assert.Equal(StaleConfidence.Confirmed, r.Confidence));
        Assert.Single(pruned!);                      // only the one confirmed row
        Assert.Single(vm.StaleRows);                 // re-scan shows the remaining likely row
    }

    [Fact]
    public void LikelyRow_RemoveCommand_PrunesJustThatRow()
    {
        IReadOnlyList<StaleDataRow>? pruned = null;
        var scanQueue = new Queue<IReadOnlyList<StaleDataRow>>([ [Likely(8)], [] ]);

        var vm = new TextTagValidationViewModel(
            scan: () => [],
            staleScan: _ => scanQueue.Dequeue(),
            prune: rows => pruned = rows,
            canCheckGameFiles: true);

        var likelyVm = vm.StaleRows.Single();
        Assert.True(likelyVm.CanRemove);
        likelyVm.RemoveCommand.Execute(null);

        Assert.Equal(8, Assert.Single(pruned!).NodeId);
        Assert.Empty(vm.StaleRows);
    }

    [Fact]
    public void CheckGameFiles_Toggle_PassesFlagToStaleScan()
    {
        bool? lastFlag = null;
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            staleScan: includeLikely => { lastFlag = includeLikely; return []; },
            canCheckGameFiles: true);

        Assert.False(lastFlag);      // initial scan defaults to confirmed-only
        vm.CheckGameFiles = true;
        Assert.True(lastFlag);       // toggling re-scans with likely enabled
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~TextTagValidationViewModelStaleTests`
Expected: FAIL — new constructor params/members do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

Create `StaleDataRowViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// One row of the Validate Text window's "Stale data" section.
public sealed partial class StaleDataRowViewModel
{
    private readonly StaleDataRow _row;
    private readonly Action<StaleDataRow>? _removeOne;

    public StaleDataRow Row => _row;
    public string ConversationName { get; }
    public string NodeLabel        { get; }
    public string CategoryLabel    { get; }
    public string ConfidenceLabel  { get; }
    public bool   IsLikely         => _row.Confidence == StaleConfidence.Likely;

    /// Per-row Remove is offered for likely rows only (confirmed rows go through
    /// the armed bulk clean-up).
    public bool CanRemove => IsLikely && _removeOne is not null;

    public StaleDataRowViewModel(StaleDataRow row, string primaryLanguage, Action<StaleDataRow>? removeOne)
    {
        _row       = row;
        _removeOne = removeOne;

        ConversationName = row.ConversationName;
        NodeLabel        = Loc.Format("VoValidation_NodeRow", row.NodeId);
        CategoryLabel    = row.Kind switch
        {
            StaleDataKind.Comment  => Loc.Get("StaleData_Category_Comment"),
            StaleDataKind.Layout   => Loc.Get("StaleData_Category_Layout"),
            _ => IsPrimary(row.Language, primaryLanguage)
                    ? Loc.Get("StaleData_Category_Translation")
                    : Loc.Format("StaleData_Category_TranslationLang", row.Language!),
        };
        ConfidenceLabel  = IsLikely
            ? Loc.Get("StaleData_Confidence_Likely")
            : Loc.Get("StaleData_Confidence_Confirmed");
    }

    private static bool IsPrimary(string? lang, string primary) =>
        string.IsNullOrEmpty(lang) ||
        string.Equals(lang, primary, StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void Remove() => _removeOne?.Invoke(_row);
}
```

Then extend `TextTagValidationViewModel`. Add fields + the new constructor parameters, initialise the stale section, and add the commands. Replace the class body's constructor and add members:

```csharp
public partial class TextTagValidationViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<TextTagIssueRow>> _scan;
    private readonly Action<string>? _addWord;

    private readonly Func<bool, IReadOnlyList<StaleDataRow>>? _staleScan;
    private readonly Action<IReadOnlyList<StaleDataRow>>? _prune;
    private readonly string _primaryLanguage;

    public ObservableCollection<TextTagRowViewModel> Rows { get; } = [];
    public ObservableCollection<StaleDataRowViewModel> StaleRows { get; } = [];

    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private bool   _hasIssues;

    [ObservableProperty] private string _staleSummaryText = string.Empty;
    [ObservableProperty] private bool   _hasStaleData;
    [ObservableProperty] private bool   _isStaleCleanUpArmed;

    public bool CanCheckGameFiles { get; }
    [ObservableProperty] private bool _checkGameFiles;

    public RelayCommand CleanUpStaleCommand        { get; }
    public RelayCommand ConfirmCleanUpStaleCommand { get; }
    public RelayCommand CancelCleanUpStaleCommand  { get; }

    public string StaleCleanUpConfirmText =>
        Loc.FormatCount("StaleData_CleanUpConfirm", ConfirmedCount);

    private int ConfirmedCount =>
        StaleRows.Count(r => !r.IsLikely);

    public TextTagValidationViewModel(
        Func<IReadOnlyList<TextTagIssueRow>> scan,
        Action<string>? addWord = null,
        Func<bool, IReadOnlyList<StaleDataRow>>? staleScan = null,
        Action<IReadOnlyList<StaleDataRow>>? prune = null,
        bool canCheckGameFiles = false,
        string primaryLanguage = "")
    {
        _scan             = scan;
        _addWord          = addWord;
        _staleScan        = staleScan;
        _prune            = prune;
        CanCheckGameFiles = canCheckGameFiles;
        _primaryLanguage  = primaryLanguage;

        CleanUpStaleCommand        = new RelayCommand(() => IsStaleCleanUpArmed = true,
                                                      () => HasStaleData && ConfirmedCount > 0 && !IsStaleCleanUpArmed);
        ConfirmCleanUpStaleCommand = new RelayCommand(ExecuteStaleCleanUp, () => IsStaleCleanUpArmed);
        CancelCleanUpStaleCommand  = new RelayCommand(() => IsStaleCleanUpArmed = false, () => IsStaleCleanUpArmed);

        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        var rows = _scan();
        Rows.Clear();
        foreach (var r in rows) Rows.Add(new TextTagRowViewModel(r, _addWord, Refresh));
        HasIssues = rows.Count > 0;
        var convCount = rows.Select(r => r.ConversationName).Distinct().Count();
        SummaryText = rows.Count == 0
            ? Loc.Get("TextTagValidation_NoIssues")
            : Loc.Format("TextTagValidation_Summary",
                Loc.FormatCount("TextTagValidation_Issues", rows.Count),
                Loc.FormatCount("TextTagValidation_Convs", convCount));

        RefreshStale();
    }

    private void RefreshStale()
    {
        IsStaleCleanUpArmed = false;
        StaleRows.Clear();
        if (_staleScan is not null)
        {
            foreach (var r in _staleScan(CheckGameFiles && CanCheckGameFiles))
                StaleRows.Add(new StaleDataRowViewModel(r, _primaryLanguage, RemoveOne));
        }
        HasStaleData = StaleRows.Count > 0;
        StaleSummaryText = StaleRows.Count == 0
            ? Loc.Get("StaleData_NoIssues")
            : Loc.FormatCount("StaleData_Summary", StaleRows.Count);
        OnPropertyChanged(nameof(StaleCleanUpConfirmText));
        RaiseStaleCommandStates();
    }

    partial void OnCheckGameFilesChanged(bool value) => RefreshStale();
    partial void OnHasStaleDataChanged(bool value) => RaiseStaleCommandStates();
    partial void OnIsStaleCleanUpArmedChanged(bool value) => RaiseStaleCommandStates();

    private void RaiseStaleCommandStates()
    {
        CleanUpStaleCommand.NotifyCanExecuteChanged();
        ConfirmCleanUpStaleCommand.NotifyCanExecuteChanged();
        CancelCleanUpStaleCommand.NotifyCanExecuteChanged();
    }

    private void ExecuteStaleCleanUp()
    {
        var confirmed = StaleRows.Where(r => !r.IsLikely).Select(r => r.Row).ToList();
        IsStaleCleanUpArmed = false;
        if (confirmed.Count == 0) return;
        _prune?.Invoke(confirmed);
        Refresh();
    }

    private void RemoveOne(StaleDataRow row)
    {
        _prune?.Invoke([row]);
        Refresh();
    }
}
```

> Keep the existing `using`s; add `using DialogEditor.ViewModels.Services;` if not present (for `StaleDataRow`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~TextTagValidationViewModelStaleTests`
Expected: PASS (4 tests). Also run the existing suite to prove the added optional params didn't break current callers:
Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~TextTagValidation`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/StaleDataRowViewModel.cs DialogEditor.ViewModels/ViewModels/TextTagValidationViewModel.cs DialogEditor.Tests/ViewModels/TextTagValidationViewModelStaleTests.cs
git commit -m "feat(hygiene): stale-data section + prune commands in Validate Text VM"
```

---

### Task 6: `MainWindowViewModel` — wire the stale scan, effective-set delegate, prune callback

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs:502-526` (`RequestTextTagValidationAsync`) + a new private helper
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelTextTagTests.cs` (add a fact)

**Interfaces:**
- Consumes: `ProjectStaleDataScanner.Scan`, `StaleDataPruner.Prune`, `TextTagValidationViewModel`'s new constructor params (Task 5); `PatchApplier.Apply`, `ConversationSnapshotBuilder.Build`, `_provider.FindConversation/LoadConversation`, `Canvas.BuildSnapshot`, `SetProject`, `SaveProject`.
- Produces: the returned `TextTagValidationViewModel` now has a live stale section wired to the current project.

- [ ] **Step 1: Write the failing test**

Model on the existing tests in `MainWindowViewModelTextTagTests` (they already build a `MainWindowViewModel`, open a project, and call `RequestTextTagValidationAsync`). Add:

```csharp
[Fact]
public async Task StaleComment_ForDeletedNode_ShownAndPrunable()
{
    // Arrange: a saved project whose open conversation's patch has a comment on a
    // deleted node id. (Reuse this file's project-open helper; if it opens a
    // conversation, delete a node and save so DeletedNodeIds is populated, OR
    // seed a patch with DeletedNodeIds + NodeComments directly via the helper.)
    var vm = OpenSavedProjectWithStaleComment(deletedNodeId: 7);

    // Act
    var window = await vm.RequestTextTagValidationAsync();

    // Assert: the stale row is present and confirmed.
    Assert.NotNull(window);
    var stale = Assert.Single(window!.StaleRows);
    Assert.False(stale.IsLikely);

    // Prune it and confirm the saved project no longer carries the comment.
    window.CleanUpStaleCommand.Execute(null);
    window.ConfirmCleanUpStaleCommand.Execute(null);
    Assert.Empty(window.StaleRows);
}
```

> Add `OpenSavedProjectWithStaleComment` next to the file's existing helpers if absent: open/create a project, put a `ConversationPatch` with `DeletedNodeIds = [7]` and `NodeComments = { [7] = "x" }` into it (via the same seam the other tests use to inject a project), and save. Ensure `ConfirmScanWithUnsavedChanges` is wired to return `ScanDirtyChoice.ScanSavedOnly` (or the project is clean) so the window opens.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~MainWindowViewModelTextTagTests.StaleComment_ForDeletedNode_ShownAndPrunable`
Expected: FAIL — `RequestTextTagValidationAsync` does not yet populate `StaleRows` (empty).

- [ ] **Step 3: Write minimal implementation**

Update `RequestTextTagValidationAsync` to pass the stale wiring, and add the effective-set helper. Replace the final `return new TextTagValidationViewModel(...)` with:

```csharp
        Func<bool, IReadOnlyList<StaleDataRow>> staleScan = includeLikely =>
        {
            if (_project is null) return [];
            var eff = includeLikely && _provider is not null ? BuildEffectiveNodeIds() : null;
            return ProjectStaleDataScanner.Scan(_project, eff);
        };

        Action<IReadOnlyList<StaleDataRow>> prune = staleRows =>
        {
            if (_project is null) return;
            SetProject(StaleDataPruner.Prune(_project, staleRows));
            SaveProject();
        };

        return new TextTagValidationViewModel(
            () => _project is null
                ? []
                : ProjectTextTagScanner.Scan(
                    _project, _activeGameId, _provider?.Language ?? "", spell: spell),
            addWord: store is null ? null : store.AddWord,
            staleScan: staleScan,
            prune: prune,
            canCheckGameFiles: _provider is not null,
            primaryLanguage: _provider?.Language ?? "");
    }

    /// Builds a conversation-name → live-node-ID-set resolver for the likely-stale
    /// pass. The open conversation uses the live canvas snapshot; others are
    /// reconstructed vanilla + patch (conflicts ignored — display semantics),
    /// returning null (skip) when a conversation can't be loaded. Mirrors
    /// VoOrphanScanner's per-conversation resolution.
    private Func<string, IReadOnlySet<int>?> BuildEffectiveNodeIds()
    {
        var provider = _provider!;
        var openConv = Canvas.ConversationName;
        var openIds  = Canvas.BuildSnapshot().Nodes.Select(n => n.NodeId).ToHashSet();

        return convName =>
        {
            if (!string.IsNullOrEmpty(openConv) && convName == openConv)
                return openIds;
            if (_project is null || !_project.Patches.TryGetValue(convName, out var patch))
                return null;
            try
            {
                var file     = provider.FindConversation(convName);
                var baseSnap = file is not null
                    ? ConversationSnapshotBuilder.Build(provider.LoadConversation(file))
                    : new ConversationEditSnapshot([]);
                var applied  = PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true);
                return applied.Nodes.Select(n => n.NodeId).ToHashSet();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Stale-data scan: could not load '{convName}': {ex.Message}");
                return null;
            }
        };
    }
```

> Ensure the `using`s at the top of `MainWindowViewModel.cs` include `DialogEditor.Core.Editing` (for `ConversationEditSnapshot`/`ConversationSnapshotBuilder`) and `DialogEditor.ViewModels.Services` — both are already used elsewhere in the file (e.g. `VoOrphanScanner` wiring), so no new import is expected.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~MainWindowViewModelTextTagTests`
Expected: PASS (new fact + existing text-tag facts).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelTextTagTests.cs
git commit -m "feat(hygiene): wire stale-data scan + prune into Validate Text request"
```

---

### Task 7: View — "Stale data" section + toggle + prune buttons + strings

**Files:**
- Modify: `DialogEditor.Avalonia/Views/TextTagValidationWindow.axaml`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/Views/TextTagValidationWindowTests.cs` (create if absent; a headless smoke test that the window builds with a stale row)

**Interfaces:**
- Consumes: `TextTagValidationViewModel` members from Task 5 (`StaleRows`, `HasStaleData`, `StaleSummaryText`, `CheckGameFiles`, `CanCheckGameFiles`, `IsStaleCleanUpArmed`, `StaleCleanUpConfirmText`, the three stale commands); `StaleDataRowViewModel` (`CategoryLabel`, `ConfidenceLabel`, `CanRemove`, `RemoveCommand`).
- Produces: no code interface — UI only.

- [ ] **Step 1: Add the resource strings**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, add (keep the file's existing `x:String` style; values are English source text):

```xml
<x:String x:Key="StaleData_SectionHeader">Stale data</x:String>
<x:String x:Key="StaleData_NoIssues">No stale data found.</x:String>
<x:String x:Key="StaleData_Summary_One">{0} stale entry</x:String>
<x:String x:Key="StaleData_Summary_Other">{0} stale entries</x:String>
<x:String x:Key="StaleData_Category_Comment">Comment</x:String>
<x:String x:Key="StaleData_Category_Layout">Layout</x:String>
<x:String x:Key="StaleData_Category_Translation">Translation</x:String>
<x:String x:Key="StaleData_Category_TranslationLang">Translation ({0})</x:String>
<x:String x:Key="StaleData_Confidence_Confirmed">Confirmed</x:String>
<x:String x:Key="StaleData_Confidence_Likely">Likely</x:String>
<x:String x:Key="StaleData_CheckGameFiles">Also check against the game files</x:String>
<x:String x:Key="StaleData_CleanUp">Remove stale data</x:String>
<x:String x:Key="StaleData_CleanUpConfirm_One">Remove {0} confirmed entry?</x:String>
<x:String x:Key="StaleData_CleanUpConfirm_Other">Remove {0} confirmed entries?</x:String>
<x:String x:Key="Button_Confirm">Confirm</x:String>
<x:String x:Key="Button_Remove">Remove</x:String>
<x:String x:Key="ToolTip_StaleData_CheckGameFiles">When a game folder is open, also flag comment/translation/layout entries whose node no longer exists in the reconstructed conversation. These are marked "Likely" because a node missing from your installed game version (but present in the version the patch targeted) can look stale.</x:String>
<x:String x:Key="ToolTip_StaleData_CleanUp">Remove all Confirmed stale entries (comments, translations, and layout positions for deleted nodes) from the saved project. This does not touch Likely entries.</x:String>
<x:String x:Key="ToolTip_StaleData_Remove">Remove this single Likely entry. It may be a false positive if your installed game version differs from the one this patch targeted — check before removing.</x:String>
```

> If `Button_Confirm` already exists in `Strings.axaml`, reuse it and drop the duplicate. Verify with: `grep -n 'Button_Confirm\|Button_Remove' DialogEditor.Avalonia/Resources/Strings.axaml` and only add the missing keys.

- [ ] **Step 2: Add the "Stale data" section to the window**

In `TextTagValidationWindow.axaml`, change the root `Grid` `RowDefinitions` from `"Auto,Auto,Auto,*,Auto"` to `"Auto,Auto,Auto,*,Auto,Auto,Auto"` and insert the stale section before the `FocusHintBar` (which moves to the last row). Add after the existing results `ScrollViewer` (Row 3):

```xml
        <!-- Stale data section header + toggle -->
        <Grid Grid.Row="4" ColumnDefinitions="*,Auto" Margin="0,4,0,2">
            <TextBlock Grid.Column="0"
                       Text="{DynamicResource StaleData_SectionHeader}"
                       Foreground="{DynamicResource Brush.Text.Primary}"
                       FontSize="{DynamicResource FontSize.Body}"
                       FontWeight="Bold" VerticalAlignment="Center"/>
            <CheckBox Grid.Column="1"
                      Content="{DynamicResource StaleData_CheckGameFiles}"
                      IsChecked="{Binding CheckGameFiles}"
                      IsEnabled="{Binding CanCheckGameFiles}"
                      FontSize="{DynamicResource FontSize.Small}"
                      ToolTip.Tip="{DynamicResource ToolTip_StaleData_CheckGameFiles}"
                      AutomationProperties.Name="{DynamicResource StaleData_CheckGameFiles}"
                      AutomationProperties.HelpText="{DynamicResource ToolTip_StaleData_CheckGameFiles}"/>
        </Grid>

        <!-- Stale data rows -->
        <ScrollViewer Grid.Row="5" MaxHeight="150" Margin="0,0,0,4">
            <Panel>
                <TextBlock Text="{DynamicResource StaleData_NoIssues}"
                           Foreground="{DynamicResource Brush.Text.Disabled}"
                           FontStyle="Italic"
                           HorizontalAlignment="Center" VerticalAlignment="Center"
                           IsVisible="{Binding !HasStaleData}"/>
                <ItemsControl ItemsSource="{Binding StaleRows}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="vm:StaleDataRowViewModel">
                            <Grid ColumnDefinitions="150,66,Auto,Auto,*,Auto" Margin="0,3">
                                <TextBlock Grid.Column="0" Text="{Binding ConversationName}"
                                           Foreground="{DynamicResource Brush.Text.Emphasis}"
                                           FontSize="{DynamicResource FontSize.Small}" FontWeight="Bold"
                                           VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
                                <TextBlock Grid.Column="1" Text="{Binding NodeLabel}"
                                           Foreground="{DynamicResource Brush.Text.Muted}"
                                           FontSize="{DynamicResource FontSize.Small}" VerticalAlignment="Center"/>
                                <TextBlock Grid.Column="2" Text="{Binding CategoryLabel}"
                                           Foreground="{DynamicResource Brush.Text.Tertiary}"
                                           FontSize="{DynamicResource FontSize.Small}" FontWeight="Bold"
                                           Width="110" VerticalAlignment="Center"/>
                                <TextBlock Grid.Column="3" Text="{Binding ConfidenceLabel}"
                                           Foreground="{DynamicResource Brush.Text.Secondary}"
                                           FontSize="{DynamicResource FontSize.Small}"
                                           Width="70" VerticalAlignment="Center"/>
                                <Button Grid.Column="5" Content="{DynamicResource Button_Remove}"
                                        Command="{Binding RemoveCommand}"
                                        IsVisible="{Binding CanRemove}"
                                        FontSize="{DynamicResource FontSize.Small}"
                                        Padding="8,2" Margin="6,0,0,0" VerticalAlignment="Center"
                                        ToolTip.Tip="{DynamicResource ToolTip_StaleData_Remove}"
                                        AutomationProperties.HelpText="{DynamicResource ToolTip_StaleData_Remove}"/>
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </Panel>
        </ScrollViewer>

        <!-- Stale data clean-up bar (armed two-click, confirmed rows only) -->
        <StackPanel Grid.Row="6" Orientation="Horizontal" Spacing="8" Margin="0,0,0,8"
                    IsVisible="{Binding HasStaleData}">
            <Button Content="{DynamicResource StaleData_CleanUp}"
                    Command="{Binding CleanUpStaleCommand}"
                    IsVisible="{Binding !IsStaleCleanUpArmed}"
                    ToolTip.Tip="{DynamicResource ToolTip_StaleData_CleanUp}"
                    AutomationProperties.HelpText="{DynamicResource ToolTip_StaleData_CleanUp}"/>
            <TextBlock Text="{Binding StaleCleanUpConfirmText}"
                       IsVisible="{Binding IsStaleCleanUpArmed}"
                       Foreground="{DynamicResource Brush.Text.Primary}"
                       VerticalAlignment="Center"/>
            <Button Content="{DynamicResource Button_Confirm}"
                    Command="{Binding ConfirmCleanUpStaleCommand}"
                    IsVisible="{Binding IsStaleCleanUpArmed}"
                    ToolTip.Tip="{DynamicResource ToolTip_StaleData_CleanUp}"
                    AutomationProperties.HelpText="{DynamicResource ToolTip_StaleData_CleanUp}"/>
            <Button Content="{DynamicResource Button_Cancel}"
                    Command="{Binding CancelCleanUpStaleCommand}"
                    IsVisible="{Binding IsStaleCleanUpArmed}"/>
        </StackPanel>
```

Then move the existing `FocusHintBar` to `Grid.Row="6"` → change it to the new last row `Grid.Row="7"`... — set it to the final row index. After adding the two rows above, the `FocusHintBar` line becomes:

```xml
        <shared:FocusHintBar Grid.Row="7" x:Name="HintBar"/>
```

Also add `xmlns` for the `vm:` namespace at the top if the `StaleDataRowViewModel` type isn't resolved — the file already declares `xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"`, which covers it.

> Note: `Button_Cancel` is assumed to already exist (used across dialogs). Verify with `grep -n 'Button_Cancel' DialogEditor.Avalonia/Resources/Strings.axaml`; add it if missing (`<x:String x:Key="Button_Cancel">Cancel</x:String>`).

- [ ] **Step 3: Add a headless view smoke test**

```csharp
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Views;

public class TextTagValidationWindowTests
{
    [AvaloniaFact]
    public void Window_BuildsAndBindsStaleRow()
    {
        var vm = new TextTagValidationViewModel(
            scan: () => [],
            staleScan: _ => [new StaleDataRow("conv_a", 7, StaleDataKind.Comment, null, StaleConfidence.Confirmed)],
            canCheckGameFiles: true);

        var window = new TextTagValidationWindow(vm);
        window.Show();

        Assert.True(vm.HasStaleData);
        Assert.Single(vm.StaleRows);
    }
}
```

> Match the pattern of existing `DialogEditor.Tests/Views/*WindowTests.cs` (they use `[AvaloniaFact]` and the headless app fixture). If `TextTagValidationWindow`'s constructor differs, mirror the existing `DiffWindowTests`/`BlameWindowTests` construction.

- [ ] **Step 4: Build + run tests**

Run: `dotnet build DialogEditor.Avalonia && dotnet test DialogEditor.Tests --filter FullyQualifiedName~TextTagValidationWindowTests`
Expected: build succeeds (no missing resource keys), test PASS. Also run the accessibility enforcers, which scan this view:
Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~AutomationHelpTextTests|FullyQualifiedName~AutomationNameTests"`
Expected: PASS (new controls carry HelpText/Name).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Views/TextTagValidationWindow.axaml DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/Views/TextTagValidationWindowTests.cs
git commit -m "feat(hygiene): Stale data section in the Validate Text window"
```

---

### Task 8: Full verification + Gaps.md update

**Files:**
- Modify: `Gaps.md` (the *Stale Patch Data Hygiene* entry under *Smaller Writer/UX Backlog*... — it is its own `###` section)

**Interfaces:** none.

- [ ] **Step 1: Run the whole test suite**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (no regressions; new tests green). Fix anything red before proceeding.

- [ ] **Step 2: Drive the app end-to-end (manual gate)**

Use the `running-the-app` skill to: open a project, delete a node that has a translator comment, save, reopen, and confirm via **Test ▸ Validate Text…** that no stale row appears (prevention). Then hand-add a stale entry (or open a project with historical rot) and confirm the "Stale data" section lists it, the armed **Remove stale data** prunes confirmed rows, and a Likely row's per-row **Remove** works. Capture a screenshot of the section.

- [ ] **Step 3: Mark the gap implemented**

Edit the *Stale Patch Data Hygiene* section in `Gaps.md` to record completion, mirroring the style of the other `✅ IMPLEMENTED (date)` entries, referencing the spec path and naming the two-tier detection + save-time prevention.

- [ ] **Step 4: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark stale patch data hygiene implemented"
```

---

## Self-Review Notes

- **Spec coverage:** detection tiers → Tasks 1–2; effective-set delegate + skip → Tasks 2, 6; prevention → Task 4; scanner purity/Loc-free (refinement: `StaleDataRow` carries no `Message`, display in VM) → Tasks 1, 5; surface as Validate Text section → Tasks 5, 7; armed bulk (confirmed) + per-row (likely) prune → Tasks 5–7; prune mechanics on patch + project layout → Task 3; localisation/tooltips/automation → Task 7; error handling (warn+skip) → Task 6; testing → every task.
- **Type consistency:** `StaleDataRow(ConversationName, NodeId, Kind, Language, Confidence)`, `Scan(project, effectiveNodeIds)`, `Prune(project, rows)`, VM ctor `(scan, addWord, staleScan, prune, canCheckGameFiles, primaryLanguage)`, and command names (`CleanUpStaleCommand`/`ConfirmCleanUpStaleCommand`/`CancelCleanUpStaleCommand`) are identical across Tasks 1, 3, 5, 6, 7.
- **Deviation from spec (intentional, surface unchanged):** the spec's scanner sketch listed a `Message` field; the plan drops it so the scanner stays Loc-free and pure, composing display text in `StaleDataRowViewModel` instead.
