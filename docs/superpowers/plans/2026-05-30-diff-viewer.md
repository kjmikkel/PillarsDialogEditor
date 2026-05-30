# Diff Viewer (read-only) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A read-only viewer that diffs a `.dialogproject` between two endpoints (working copy and/or git refs), lists the conversations that changed with +/~/− counts, and shows the selected conversation on a read-only canvas with added/changed/removed nodes tinted.

**Architecture:** Pure, git-free diff logic in `DialogEditor.Patch/Diff/` (a value-aware comparison of two projects' patches — no game folder needed for the list). A small `IGitRunner` shells out to `git show <ref>:<path>` to load a project at a ref. The UI is a `DiffWindow` (endpoint pickers → changed-conversations list → read-only canvas overlay reusing the existing canvas via a new `DiffStatus` on `NodeViewModel`).

**Tech Stack:** C# 12 / .NET 8, CommunityToolkit.Mvvm, Avalonia 11.3, xUnit, `Avalonia.Headless.XUnit`.

---

## Context

`.dialogproject` files are version-controlled, but there's no in-app way to see *what changed* in a conversation between two points in history. This implements **Spec 1** of the diff-viewing gap (`docs/superpowers/specs/2026-05-30-diff-viewer-design.md`): a read-only viewer. **Selective apply is Spec 2** and is out of scope here.

Key fact: a `.dialogproject` stores **patches** (per-conversation diffs), so the changed-conversations list and counts are a value-aware diff of two projects' patches — **no game folder required**. The game folder is only needed to reconstruct the full graph for the canvas overlay.

### Decisions locked during brainstorming
- Presentation: changed-conversations list → drill into a read-only canvas overlay (Layout B).
- Endpoints: any two of {working copy, git ref}.
- Read-only (no apply); git access via shelling to `git` (no new dependency).
- Canvas: reuse the existing canvas via a `DiffStatus` on `NodeViewModel`.

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `DialogEditor.Patch/Diff/IGitRunner.cs` | Create | `IGitRunner` + `GitResult`; abstraction over running git. |
| `DialogEditor.Patch/Diff/ProcessGitRunner.cs` | Create | Real `IGitRunner` via `System.Diagnostics.Process`. |
| `DialogEditor.Patch/Diff/DiffEndpoint.cs` | Create | `DiffEndpoint` (WorkingCopy \| GitRef). |
| `DialogEditor.Patch/Diff/ProjectVersionLoader.cs` | Create | Resolve an endpoint → `DialogProject`. |
| `DialogEditor.Patch/Diff/ConversationChange.cs` | Create | Per-conversation change record. |
| `DialogEditor.Patch/Diff/ProjectDiff.cs` | Create | Pure value-aware diff of two projects → changes. |
| `DialogEditor.Patch/Diff/DiffException.cs` | Create | Thrown for git/loading failures (carries a message). |
| `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs` | Create | Endpoint selection, changed list, selection. |
| `DialogEditor.Avalonia/Views/DiffWindow.axaml(.cs)` | Create | Endpoint pickers, changed list, canvas area. |
| `DialogEditor.ViewModels/ViewModels/NodeViewModel.cs` | Modify | Add `DiffStatus` property. |
| `DialogEditor.Avalonia/Converters/DiffStatusToBrushConverter.cs` | Create | Tint nodes by diff status. |
| `DialogEditor.Avalonia/Views/MainWindow.axaml(.cs)` | Modify | "View › Compare Versions…" entry point. |
| `DialogEditor.Avalonia/Resources/Strings.axaml` | Modify | Localized strings for the window/menu/errors. |
| `DialogEditor.Tests/Patch/Diff/*Tests.cs` | Create | Unit tests for loader/diff. |
| `DialogEditor.Tests/ViewModels/DiffViewModelTests.cs` | Create | Headless VM tests. |
| `DialogEditor.Tests/Views/DiffWindowTests.cs` | Create | Headless window smoke tests. |

## Reusable existing code
- `DialogProjectSerializer.Deserialize(string)` — `DialogEditor.Patch/DialogProjectSerializer.cs`.
- `DialogProject`, `ConversationPatch`, `NodeModification`, `NodeTranslation` — `DialogEditor.Patch/` & `DialogEditor.Core/Models`.
- `GitMergeAnalyzer` (value-aware comparison style) — `DialogEditor.Patch/GitConflict/` (model `ProjectDiff` on it).
- `TextDiff` — `DialogEditor.Patch/GitConflict/TextDiff.cs` (for before/after node detail in Stage 3).
- Canvas reconstruction path used by `MainWindowViewModel.LoadNewConversation`: `PatchApplier.Apply` + `ConversationSnapshotBuilder.ToConversation` + `ConversationViewModel.Load`.
- Conventions: `[AvaloniaFact]`; new `<Window>` needs a public parameterless ctor (AVLN3000 guard) + `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`; tooltips on every control; strings from `Strings.axaml`; `AppLog` on every caught exception; in headless tests use `Command!.Execute(null)`/`CanExecute(null)`.

---

# Stage 1 — Pure diff backend (no UI)

### Task 1: `IGitRunner` + `GitResult`

**Files:**
- Create: `DialogEditor.Patch/Diff/IGitRunner.cs`

- [ ] **Step 1: Implement** (no test — pure interface/record)

```csharp
namespace DialogEditor.Patch.Diff;

public record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// Abstraction over running git, so loaders are testable without a real repo.
public interface IGitRunner
{
    GitResult Run(string workingDirectory, params string[] args);
}
```

- [ ] **Step 2: Commit** — `feat: IGitRunner abstraction for diff viewer` (trailing `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`)

---

### Task 2: `DiffEndpoint` + `DiffException`

**Files:**
- Create: `DialogEditor.Patch/Diff/DiffEndpoint.cs`
- Create: `DialogEditor.Patch/Diff/DiffException.cs`

- [ ] **Step 1: Implement**

```csharp
// DiffEndpoint.cs
namespace DialogEditor.Patch.Diff;

/// One side of a comparison: the on-disk working copy, or a git ref (branch/commit).
public abstract record DiffEndpoint
{
    public sealed record WorkingCopy : DiffEndpoint;
    public sealed record GitRef(string Ref) : DiffEndpoint;
}
```

```csharp
// DiffException.cs
namespace DialogEditor.Patch.Diff;

/// Thrown when an endpoint cannot be loaded (git unavailable, bad ref, file not
/// tracked, unreadable project). Message is safe to show to the user.
public sealed class DiffException(string message) : Exception(message);
```

- [ ] **Step 2: Commit** — `feat: DiffEndpoint and DiffException`

---

### Task 3: `ProjectVersionLoader`

**Files:**
- Create: `DialogEditor.Patch/Diff/ProjectVersionLoader.cs`
- Test: `DialogEditor.Tests/Patch/Diff/ProjectVersionLoaderTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class ProjectVersionLoaderTests
{
    // Records the args git was asked to run and returns canned output.
    private sealed class FakeGit : IGitRunner
    {
        public List<string[]> Calls { get; } = [];
        public Func<string[], GitResult> Handler = _ => new GitResult(0, "", "");
        public GitResult Run(string workingDirectory, params string[] args)
        {
            Calls.Add(args);
            return Handler(args);
        }
    }

    private static string ProjectJson(string name) =>
        DialogProjectSerializer.Serialize(DialogProject.Empty(name));

    [Fact]
    public void WorkingCopy_ReadsFileFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wc_{Guid.NewGuid():N}.dialogproject");
        File.WriteAllText(path, ProjectJson("WC"));
        try
        {
            var loader = new ProjectVersionLoader(new FakeGit());
            var project = loader.Load(new DiffEndpoint.WorkingCopy(), path);
            Assert.Equal("WC", project.Name);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GitRef_RunsGitShowAndDeserializes()
    {
        var fake = new FakeGit();
        fake.Handler = args =>
            args is ["rev-parse", "--show-toplevel"]
                ? new GitResult(0, "C:/repo\n", "")
                : new GitResult(0, ProjectJson("AtRef"), "");

        var loader = new ProjectVersionLoader(fake);
        var project = loader.Load(new DiffEndpoint.GitRef("main"), "C:/repo/mods/my.dialogproject");

        Assert.Equal("AtRef", project.Name);
        Assert.Contains(fake.Calls, c => c.Length == 2 && c[0] == "show"
            && c[1] == "main:mods/my.dialogproject");
    }

    [Fact]
    public void GitRef_NonZeroExit_ThrowsDiffException()
    {
        var fake = new FakeGit
        {
            Handler = args => args is ["rev-parse", "--show-toplevel"]
                ? new GitResult(0, "C:/repo\n", "")
                : new GitResult(128, "", "fatal: invalid object name 'nope'")
        };
        var loader = new ProjectVersionLoader(fake);

        var ex = Assert.Throws<DiffException>(() =>
            loader.Load(new DiffEndpoint.GitRef("nope"), "C:/repo/mods/my.dialogproject"));
        Assert.Contains("nope", ex.Message);
    }
}
```

- [ ] **Step 2: Run — verify fail**
Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ProjectVersionLoaderTests"`

- [ ] **Step 3: Implement**

```csharp
namespace DialogEditor.Patch.Diff;

public class ProjectVersionLoader(IGitRunner git)
{
    /// Loads the project for `endpoint`. `projectFilePath` is the working-copy path,
    /// used both to read the working copy and to locate the repo + relative path for refs.
    public DialogProject Load(DiffEndpoint endpoint, string projectFilePath)
    {
        var json = endpoint switch
        {
            DiffEndpoint.WorkingCopy   => ReadWorkingCopy(projectFilePath),
            DiffEndpoint.GitRef gitRef => ReadAtRef(gitRef.Ref, projectFilePath),
            _ => throw new DiffException("Unknown diff endpoint."),
        };

        try { return DialogProjectSerializer.Deserialize(json); }
        catch (Exception ex) { throw new DiffException($"Could not read project: {ex.Message}"); }
    }

    private static string ReadWorkingCopy(string path)
    {
        if (!File.Exists(path)) throw new DiffException($"Working-copy file not found: {path}");
        return File.ReadAllText(path);
    }

    private string ReadAtRef(string gitRef, string projectFilePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(projectFilePath))
                  ?? throw new DiffException("Project path has no directory.");

        var root = git.Run(dir, "rev-parse", "--show-toplevel");
        if (!root.Ok)
            throw new DiffException("Not a git repository (or git is not installed).");

        var repoRoot = root.StdOut.Trim();
        var relative = Path.GetRelativePath(repoRoot, Path.GetFullPath(projectFilePath))
                           .Replace('\\', '/');

        var show = git.Run(dir, "show", $"{gitRef}:{relative}");
        if (!show.Ok)
            throw new DiffException($"Could not read '{relative}' at '{gitRef}': {show.StdErr.Trim()}");

        return show.StdOut;
    }
}
```

- [ ] **Step 4: Run — verify pass.**
- [ ] **Step 5: Commit** — `feat: ProjectVersionLoader — load .dialogproject from working copy or git ref`

---

### Task 4: `ConversationChange` + `ProjectDiff`

**Files:**
- Create: `DialogEditor.Patch/Diff/ConversationChange.cs`
- Create: `DialogEditor.Patch/Diff/ProjectDiff.cs`
- Test: `DialogEditor.Tests/Patch/Diff/ProjectDiffTests.cs`

**Behavior:** For each conversation present in either project, build a per-node *signature* from that side's patch (a node's full contribution: added snapshot, modification, deletion flag, and its translations), then compare:
- **Added** = node ids whose signature exists in `b` but not `a`.
- **Removed** = node ids whose signature exists in `a` but not `b`.
- **Modified** = node ids present in both with **different** signatures.
A conversation with no added/removed/modified nodes is omitted.

- [ ] **Step 1: Implement `ConversationChange`**

```csharp
namespace DialogEditor.Patch.Diff;

public record ConversationChange(
    string             Name,
    IReadOnlyList<int> Added,
    IReadOnlyList<int> Removed,
    IReadOnlyList<int> Modified)
{
    public int  AddedCount    => Added.Count;
    public int  RemovedCount  => Removed.Count;
    public int  ModifiedCount => Modified.Count;
    public bool HasChanges    => Added.Count + Removed.Count + Modified.Count > 0;
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class ProjectDiffTests
{
    private static NodeEditSnapshot Node(int id, string display = "Conversation") =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", display, "None", "", "", "", false, false, [], [], []);

    private static DialogProject WithAdded(params NodeEditSnapshot[] nodes) =>
        DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, nodes, [], []));

    [Fact]
    public void AddedNode_InBOnly_IsAdded()
    {
        var a = WithAdded(Node(1));
        var b = WithAdded(Node(1), Node(2));

        var change = Assert.Single(ProjectDiff.Diff(a, b));
        Assert.Equal("greeting", change.Name);
        Assert.Equal([2], change.Added);
        Assert.Empty(change.Removed);
        Assert.Empty(change.Modified);
    }

    [Fact]
    public void NodeInAOnly_IsRemoved()
    {
        var a = WithAdded(Node(1), Node(2));
        var b = WithAdded(Node(1));

        var change = Assert.Single(ProjectDiff.Diff(a, b));
        Assert.Equal([2], change.Removed);
    }

    [Fact]
    public void SameNodeDifferentContent_IsModified()
    {
        var a = WithAdded(Node(1, "Conversation"));
        var b = WithAdded(Node(1, "QuestionNode"));

        var change = Assert.Single(ProjectDiff.Diff(a, b));
        Assert.Equal([1], change.Modified);
    }

    [Fact]
    public void IdenticalProjects_HaveNoChanges()
    {
        var a = WithAdded(Node(1));
        var b = WithAdded(Node(1));
        Assert.Empty(ProjectDiff.Diff(a, b));
    }

    [Fact]
    public void TranslationChange_IsModified()
    {
        DialogProject WithText(string text) =>
            DialogProject.Empty("p").WithPatch(
                new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [], [])
                {
                    Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                    { ["en"] = [new NodeTranslation(5, text, "")] }
                });

        var change = Assert.Single(ProjectDiff.Diff(WithText("Hi"), WithText("Hello")));
        Assert.Equal([5], change.Modified);
    }
}
```

- [ ] **Step 3: Run — verify fail.**

- [ ] **Step 4: Implement `ProjectDiff`**

```csharp
using System.Text.Json;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch.Diff;

public static class ProjectDiff
{
    public static List<ConversationChange> Diff(DialogProject a, DialogProject b)
    {
        var changes = new List<ConversationChange>();
        var names = a.Patches.Keys.Concat(b.Patches.Keys).Distinct();

        foreach (var name in names)
        {
            var sigA = Signatures(a.Patches.GetValueOrDefault(name));
            var sigB = Signatures(b.Patches.GetValueOrDefault(name));

            var added    = sigB.Keys.Where(id => !sigA.ContainsKey(id)).OrderBy(id => id).ToList();
            var removed  = sigA.Keys.Where(id => !sigB.ContainsKey(id)).OrderBy(id => id).ToList();
            var modified = sigB.Keys
                .Where(id => sigA.TryGetValue(id, out var s) && !string.Equals(s, sigB[id], StringComparison.Ordinal))
                .OrderBy(id => id).ToList();

            if (added.Count + removed.Count + modified.Count > 0)
                changes.Add(new ConversationChange(name, added, removed, modified));
        }

        return changes.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
    }

    // nodeId -> JSON signature of that node's full contribution to the patch.
    private static Dictionary<int, string> Signatures(ConversationPatch? patch)
    {
        var map = new Dictionary<int, string>();
        if (patch is null) return map;

        var added    = new Dictionary<int, NodeEditSnapshot>();
        foreach (var n in patch.AddedNodes) added[n.NodeId] = n;

        var modified = new Dictionary<int, NodeModification>();
        foreach (var m in patch.ModifiedNodes) modified[m.NodeId] = m;

        var deleted = patch.DeletedNodeIds.ToHashSet();

        var translations = new Dictionary<int, List<NodeTranslation>>();
        foreach (var (lang, list) in patch.Translations)
            foreach (var t in list)
                (translations.TryGetValue(t.NodeId, out var l) ? l : translations[t.NodeId] = []).Add(t);

        var ids = added.Keys
            .Concat(modified.Keys).Concat(deleted).Concat(translations.Keys)
            .Distinct();

        foreach (var id in ids)
        {
            map[id] = JsonSerializer.Serialize(new
            {
                Added    = added.GetValueOrDefault(id),
                Modified = modified.GetValueOrDefault(id),
                Deleted  = deleted.Contains(id),
                Text     = translations.GetValueOrDefault(id),
            });
        }
        return map;
    }
}
```

- [ ] **Step 5: Run — verify pass; then run the full suite** (`dotnet test DialogEditor.Tests`).
- [ ] **Step 6: Commit** — `feat: ProjectDiff — value-aware per-conversation change set`

---

### Task 5: `ProcessGitRunner`

**Files:**
- Create: `DialogEditor.Patch/Diff/ProcessGitRunner.cs`

No unit test (wraps `Process`; exercised manually + by integration). Keep it tiny.

- [ ] **Step 1: Implement**

```csharp
using System.Diagnostics;

namespace DialogEditor.Patch.Diff;

public sealed class ProcessGitRunner : IGitRunner
{
    public GitResult Run(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory       = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi)
                ?? throw new DiffException("Could not start git.");
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return new GitResult(p.ExitCode, stdout, stderr);
        }
        catch (Exception ex) when (ex is not DiffException)
        {
            // git missing / not on PATH
            throw new DiffException($"git is not available: {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Build** (`dotnet build DialogEditor.Patch`) and **Commit** — `feat: ProcessGitRunner`

---

# Stage 2 — Endpoint pickers + changed-conversations list (no game folder needed)

### Task 6: `DiffViewModel`

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/DiffViewModelTests.cs`

**Surface:**
- Ctor `(IGitRunner git, string projectFilePath, IGameDataProvider? provider = null, string language = "en")`. `provider`/`language` are used only by the Stage 3 canvas overlay; Stage 2 ignores them, so pass them from the start to keep the ctor stable.
- `IReadOnlyList<EndpointOption> EndpointOptions` — built from: `WorkingCopy`, then local branches (`git branch --format=%(refname:short)`), then recent commits (`git log -n 20 --format=%h %s`). Each `EndpointOption { string Label; DiffEndpoint Endpoint; }`. If git fails, the list still contains `WorkingCopy` and the VM exposes a `StatusText` explaining git was unavailable (logged via `AppLog`).
- `[ObservableProperty] EndpointOption? _leftEndpoint;` and `_rightEndpoint;` (defaults: left = first git ref if any else WorkingCopy; right = WorkingCopy).
- `IReadOnlyList<ConversationChange> Changes` — recomputed when either endpoint changes: load both via `ProjectVersionLoader`, run `ProjectDiff`. On `DiffException`, set `StatusText`, clear `Changes`.
- `[ObservableProperty] ConversationChange? _selected;`
- `string StatusText` (observable).

- [ ] **Step 1: Write failing tests** (use a fake `IGitRunner` returning two project JSONs and a temp working-copy file):
  - Selecting two endpoints whose projects differ by one added node → `Changes` has one `ConversationChange` with `AddedCount == 1`.
  - A `DiffException` (git ref fails) → `Changes` empty and `StatusText` non-empty (assert it equals the localized key via `StubStringProvider`).
- [ ] **Step 2: Run — verify fail.**
- [ ] **Step 3: Implement** with `[ObservableProperty]`/`partial void On…Changed` recompute hooks; localize all status via `Loc`.
- [ ] **Step 4: Run — verify pass.**
- [ ] **Step 5: Commit** — `feat: DiffViewModel — endpoints and changed-conversations list`

### Task 7: `DiffWindow` + entry point + strings

**Files:**
- Create: `DialogEditor.Avalonia/Views/DiffWindow.axaml` + `.axaml.cs`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml` (a "View › Compare Versions…" menu item) + `.axaml.cs` (open the window)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/Views/DiffWindowTests.cs`

**Layout:** top row — two `ComboBox` endpoint pickers (`ItemsSource={Binding EndpointOptions}`, display `Label`, `SelectedItem` two-way to Left/Right). Left — `ListBox` bound to `Changes` showing `Name` + counts (`+{AddedCount} ~{ModifiedCount} −{RemovedCount}`), `SelectedItem` two-way to `Selected`. Right — a placeholder panel for the canvas (filled in Stage 3); for now a `TextBlock` showing `Selected.Name`. Status bar bound to `StatusText`.

**Conventions:** app icon; tooltips on both pickers and the list; all strings via `Strings.axaml` (`DiffWindow_Title`, `DiffWindow_LeftLabel`, `DiffWindow_RightLabel`, `DiffWindow_ChangedHeader`, `DiffWindow_CountsTooltip`, `DiffWindow_NoGameFolder`, `Menu_CompareVersions`, plus `Status_*` keys used by the VM); public parameterless ctor; `AppLog` on errors. The menu item is enabled only when a project is open (bind to `IsProjectOpen`) and passes `_projectPath` + a `ProcessGitRunner` to the VM.

- [ ] **Step 1: Implement window + VM wiring in `MainWindow.axaml.cs`:**

```csharp
private void CompareVersions_Click(object? sender, RoutedEventArgs e)
{
    var vm = (MainWindowViewModel)DataContext!;
    if (vm.ProjectPath is null) return;          // expose _projectPath as a public getter
    var diffVm = new DiffViewModel(new ProcessGitRunner(), vm.ProjectPath,
                                   vm.Provider, vm.Provider?.Language ?? "en");
    new DiffWindow(diffVm).Show();
}
```
(Add a `public string? ProjectPath => _projectPath;` getter to `MainWindowViewModel`.)

- [ ] **Step 2: Write headless tests** (`[AvaloniaFact]`): construct the window with a `DiffViewModel` (fake git, temp file) holding one change; `window.Show()`; assert the changed `ListBox` (`x:Name="ChangedList"`) has one item.
- [ ] **Step 3: Run filtered tests + full suite — verify pass** (watch for AVLN3000; the parameterless ctor prevents it).
- [ ] **Step 4: Commit** — `feat: DiffWindow + Compare Versions entry point`

---

# Stage 3 — Read-only canvas overlay + node detail

### Task 8: `DiffStatus` on `NodeViewModel` + converter

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/NodeViewModel.cs`
- Create: `DialogEditor.Avalonia/Converters/DiffStatusToBrushConverter.cs`
- Test: `DialogEditor.Tests/ViewModels/NodeViewModelTests.cs` (or a new converter test)

- [ ] **Step 1: Add the enum + property.** In `DialogEditor.ViewModels` (e.g. a `DiffStatus.cs` or atop `NodeViewModel.cs`):

```csharp
public enum DiffStatus { Unchanged, Added, Removed, Changed }
```
On `NodeViewModel`, add an observable (it is a manual `ObservableObject` — follow the file's existing pattern):
```csharp
private DiffStatus _diffStatus = DiffStatus.Unchanged;
public DiffStatus DiffStatus
{
    get => _diffStatus;
    set { _diffStatus = value; OnPropertyChanged(nameof(DiffStatus)); }
}
```

- [ ] **Step 2: Write a converter test** (mirror an existing converter test in `DialogEditor.Tests`): `Added → green`, `Removed → red`, `Changed → amber`, `Unchanged → transparent/null`.
- [ ] **Step 3: Implement `DiffStatusToBrushConverter : IValueConverter`** returning `SolidColorBrush` per status (green `#3a7a3a`, amber `#c08a2a`, red `#7a2a2a`, Unchanged → `Brushes.Transparent`). Register it in the resource dictionary alongside the other converters.
- [ ] **Step 4: Run — verify pass.**
- [ ] **Step 5: Commit** — `feat: DiffStatus on NodeViewModel + tint converter`

### Task 9: Render the diff on the canvas + before/after detail

**Files:**
- Modify: `DialogEditor.Avalonia/Views/DiffWindow.axaml(.cs)` (host the read-only canvas)
- Modify: `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs` (build the conversation to display)
- Test: `DialogEditor.Tests/ViewModels/DiffViewModelTests.cs`

**Behavior:** when `Selected` changes and a game provider is available (passed into the VM), reconstruct the **right (new) endpoint's** conversation using the same path `MainWindowViewModel.LoadNewConversation` uses (`PatchApplier.Apply` over the provider's base snapshot + `ConversationSnapshotBuilder.ToConversation`), load it into a read-only `ConversationViewModel`, then set each `NodeViewModel.DiffStatus` from the `ConversationChange` (`Added`→Added, `Modified`→Changed). For `Removed` ids, build the node from the **left** endpoint and add it with `DiffStatus.Removed` (ghosted). Selecting a node shows old-vs-new field/text via `TextDiff` in a detail sub-panel.

> Note: the VM already received `IGameDataProvider? provider` + `language` in its ctor (Stage 2, Task 6). When `provider` is null, skip canvas reconstruction and show the localized `DiffWindow_NoGameFolder` hint (the changed list still works).

- [ ] **Step 1: Write failing tests** at the VM level: given a provider stub and a selected change with one added node, `BuildDiffConversation()` returns a `ConversationViewModel` whose node with that id has `DiffStatus.Added`. (Reuse existing provider/canvas test stubs.)
- [ ] **Step 2: Run — verify fail.**
- [ ] **Step 3: Implement** the reconstruction + status assignment in the VM; bind the canvas control in `DiffWindow.axaml` to the produced `ConversationViewModel` in read-only mode (set `Canvas.IsEditable = false`).
- [ ] **Step 4: Run — verify pass; run full suite.**
- [ ] **Step 5: Update `Gaps.md`** — mark diff viewing (read-only) implemented; note selective apply (Spec 2) and branch/history navigation remain.
- [ ] **Step 6: Commit** — `feat: read-only diff canvas overlay + before/after node detail; update Gaps`

---

## Verification

1. `dotnet test DialogEditor.Tests` — all pass (existing + new).
2. `dotnet build DialogEditor.Avalonia` — 0 errors (no AVLN3000).
3. **Manual — list:** open a project in a git repo, **View › Compare Versions…**, pick `working copy` vs `main` → changed conversations list shows with correct +/~/− counts.
4. **Manual — canvas:** with a game folder open, select a changed conversation → canvas shows the graph with added (green) / changed (amber) / removed (ghosted red) nodes; selecting a changed node shows before/after text highlighted.
5. **Manual — errors:** pick a non-existent ref → friendly status, no crash; run outside a git repo → friendly status.
6. **Manual — no game folder:** the changed list still works; the canvas area shows the "open a game folder" hint.

## Notes for the executor
- Honor `CLAUDE.md`: strict TDD; no hard-coded user-visible strings; tooltips on new controls; every `catch` logs via `AppLog` (except `OperationCanceledException`).
- Commit directly to `main` unless told otherwise (project convention).
- `ProjectDiff`'s signature-based comparison is intentionally a diff of the *projects' patches* (what each version changed), which needs no game files — this is correct for the list. The canvas overlay (Stage 3) is the only part needing a game folder.
- **Spec 2 (selective apply) is out of scope.** Do not add apply/mutation here.
