# Close Project Command Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** **File ▸ Close Project** (Ctrl+W) closes the current project — dirty-guarded, canvas cleared, auto-reopen memory cleared — returning the editor to projectless browse mode.

**Architecture:** Extract the project teardown already inlined in `ReloadCurrentProjectFromDisk`'s vanished-file branch into a shared `CloseProjectCore(string statusText)`; the new `CloseProjectCommand` wraps it in the existing `GuardDirtyThen` unsaved-changes guard and adds the two user-intent extras (clear canvas, clear `AppSettings.LastProjectPath`) that the branch-switch path deliberately does NOT do. Canvas clearing needs a new public `ConversationViewModel.Clear()` extracted from the reset block at the top of `Load()`.

**Tech Stack:** C# / .NET 8, Avalonia, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-05-close-project-design.md`

## Global Constraints

- Strict red/green TDD — failing test before implementation (CLAUDE.md).
- No user-visible text hard-coded in C#/XAML — all new strings go in `DialogEditor.Avalonia\Resources\Strings.axaml` (CLAUDE.md localisation rule).
- Every new interactive control carries a detailed `ToolTip` + `AutomationProperties.HelpText` (CLAUDE.md UI rule).
- `DialogEditor.Tests` runs serially — do not change test parallelisation.
- Branch-switch semantics preserved: `ReloadCurrentProjectFromDisk` must NOT clear `AppSettings.LastProjectPath` or the canvas.
- Keyboard shortcuts are dispatched in `MainWindow.axaml.cs` `OnKeyDownTunnel` (NOT Avalonia `KeyBinding`s — the spec's original `KeyBinding` mention is amended in Task 3); `InputGesture` on a `MenuItem` is display-only.

---

### Task 1: `ConversationViewModel.Clear()` (TDD)

**Files:**
- Create: `DialogEditor.Tests\ViewModels\ConversationViewModelClearTests.cs`
- Modify: `DialogEditor.ViewModels\ViewModels\ConversationViewModel.cs` (`Load()` at ~line 303)

**Interfaces:**
- Consumes: existing `Load(Conversation, ConversationEditSnapshot?)`, `AddNode(NodeViewModel, LayoutPoint)`, test helpers `StubDispatcher`, `StubStringProvider`.
- Produces: `public void Clear()` on `ConversationViewModel` — resets the canvas to the empty no-conversation state. Task 2's `DoCloseProject` calls `Canvas.Clear()`.

- [x] **Step 1: Write the failing tests**

Create `DialogEditor.Tests\ViewModels\ConversationViewModelClearTests.cs`:

```csharp
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Clear() resets the canvas to the empty no-conversation state — used by
/// Close Project (the closed project's patched content must not stay visible).
/// Spec: docs/superpowers/specs/2026-07-05-close-project-design.md
public class ConversationViewModelClearTests
{
    public ConversationViewModelClearTests()
    {
        Loc.Configure(new StubStringProvider());
    }

    private static ConversationViewModel MakeVm() =>
        new(new StubDispatcher());

    private static NodeViewModel MakeNode(int id) =>
        new(new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [],
            [], [], "Conversation", "None"), null);

    [Fact]
    public void Clear_EmptiesCanvasAndResetsState()
    {
        var vm = MakeVm();
        vm.Load(new Conversation("tavern", [], StringTable.Empty));
        vm.AddNode(MakeNode(1), new LayoutPoint(0, 0));   // populates + dirties + pushes undo

        vm.Clear();

        Assert.Empty(vm.Nodes);
        Assert.Empty(vm.Connections);
        Assert.Empty(vm.Annotations);
        Assert.False(vm.IsModified);
        Assert.Null(vm.SelectedNode);
        Assert.Equal("", vm.ConversationName);
        Assert.Null(vm.BaseSnapshot);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void Load_AfterClear_StillWorks()
    {
        var vm = MakeVm();
        vm.Clear();

        vm.Load(new Conversation("tavern", [], StringTable.Empty));

        Assert.Equal("tavern", vm.ConversationName);
        Assert.False(vm.IsModified);
    }
}
```

- [x] **Step 2: Run to verify red**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConversationViewModelClearTests"`
Expected: FAIL to compile — `'ConversationViewModel' does not contain a definition for 'Clear'`.

- [x] **Step 3: Implement `Clear()` by extraction**

In `DialogEditor.ViewModels\ViewModels\ConversationViewModel.cs`, the top of `Load()` currently reads:

```csharp
    public void Load(Conversation conversation, ConversationEditSnapshot? baseSnapshot = null)
    {
        ConversationName = conversation.Name;
        _searchCts?.Cancel();
        _undoStack.Clear();
        IsModified  = false;
        Nodes.Clear();
        Connections.Clear();
        Annotations.Clear();
        SelectedNode = null;
        SearchQuery  = string.Empty;
        BaseSnapshot = null;
        RefreshUndoRedo();
```

Replace with (the reset block moves verbatim into `Clear()`; `ConversationName` is
assigned after the reset instead of before, which nothing in the block reads):

```csharp
    /// Resets the canvas to the empty no-conversation state: no nodes, clean
    /// undo/dirty state, no baseline. Used by Close Project — a closed project's
    /// patched content must not stay visible — and by Load() before populating.
    public void Clear()
    {
        ConversationName = "";
        _searchCts?.Cancel();
        _undoStack.Clear();
        IsModified  = false;
        Nodes.Clear();
        Connections.Clear();
        Annotations.Clear();
        SelectedNode = null;
        SearchQuery  = string.Empty;
        BaseSnapshot = null;
        RefreshUndoRedo();
    }

    public void Load(Conversation conversation, ConversationEditSnapshot? baseSnapshot = null)
    {
        Clear();
        ConversationName = conversation.Name;
```

(The doc comment for `baseSnapshot` stays on `Load` unchanged.)

- [x] **Step 4: Run the new tests, then the full suite**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConversationViewModelClearTests"`
Expected: PASS (2).

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (1896).

- [x] **Step 5: Commit**

```powershell
git add DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs DialogEditor.Tests/ViewModels/ConversationViewModelClearTests.cs
git commit -m @'
feat(canvas): extract public Clear() from ConversationViewModel.Load

Close Project needs to blank the canvas without loading a replacement
conversation; Load() now delegates to the extracted reset.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 2: `CloseProjectCommand` + shared `CloseProjectCore` (TDD)

**Files:**
- Create: `DialogEditor.Tests\ViewModels\MainWindowViewModelCloseProjectTests.cs`
- Modify: `DialogEditor.ViewModels\ViewModels\MainWindowViewModel.cs` (`ReloadCurrentProjectFromDisk` ~line 542, `SetProject` ~line 241, new members near the New/Open commands ~line 494)

**Interfaces:**
- Consumes: `Canvas.Clear()` (Task 1); existing `GuardDirtyThen(Action)`, `SetProject(DialogProject?)`, `Detail.Clear()`, `AppSettings.LastProjectPath` (nullable setter); test helpers `StubDispatcher`, `StubFolderPicker`, `StubFilePicker`, `StubStringProvider`.
- Produces: `CloseProjectCommand` (`IRelayCommand`, `CanExecute` = project open) — Task 3 binds the menu item and key handler to it. Private `CloseProjectCore(string statusText)` shared with `ReloadCurrentProjectFromDisk`.

- [x] **Step 1: Write the failing tests**

Create `DialogEditor.Tests\ViewModels\MainWindowViewModelCloseProjectTests.cs`:

```csharp
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// File > Close Project: dirty-guarded teardown to projectless browse mode.
/// The canvas is cleared (patched content exists nowhere after close) and
/// AppSettings.LastProjectPath is cleared (a deliberate close sticks across
/// restarts). The branch-switch teardown keeps both — guarded here too.
/// Spec: docs/superpowers/specs/2026-07-05-close-project-design.md
public class MainWindowViewModelCloseProjectTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _tempDir;

    public MainWindowViewModelCloseProjectTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_close_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _tempDir = Path.Combine(Path.GetTempPath(), $"close_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_tempDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static Task InvokeLoadProjectAsync(MainWindowViewModel vm, string path)
    {
        var mi = typeof(MainWindowViewModel).GetMethod("LoadProjectAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Task)mi.Invoke(vm, [path, false])!;
    }

    /// Writes an empty project named after the file into _tempDir; returns its path.
    private string WriteProject(string relativePath)
    {
        var path = Path.Combine(_tempDir, relativePath);
        DialogProjectSerializer.SaveToFile(
            path, DialogProject.Empty(Path.GetFileNameWithoutExtension(path)));
        return path;
    }

    private static NodeViewModel MakeNode(int id) =>
        new(new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [],
            [], [], "Conversation", "None"), null);

    private async Task<MainWindowViewModel> OpenVmWithCanvasContent(string projectPath)
    {
        var vm = MakeVm();
        await InvokeLoadProjectAsync(vm, projectPath);
        vm.Canvas.AddNode(MakeNode(1), new LayoutPoint(0, 0));
        vm.CurrentConversationName = "some_conv";
        vm.IsModified = false;   // AddNode dirtied the canvas; start the test clean
        return vm;
    }

    // ── Close behaviour ───────────────────────────────────────────────────

    [Fact]
    public async Task Close_ClearsProjectStateCanvasAndLastPath()
    {
        var path = WriteProject("p.dialogproject");
        var vm   = await OpenVmWithCanvasContent(path);

        vm.CloseProjectCommand.Execute(null);

        Assert.False(vm.IsProjectOpen);
        Assert.Null(vm.ProjectPath);
        Assert.Null(vm.CurrentProjectName);
        Assert.Null(vm.CurrentConversationName);
        Assert.Empty(vm.Canvas.Nodes);
        Assert.False(vm.IsModified);
        Assert.Null(AppSettings.LastProjectPath);
    }

    [Fact]
    public async Task Close_DirtyCanvas_DefersUntilProceed()
    {
        var path = WriteProject("p.dialogproject");
        var vm   = await OpenVmWithCanvasContent(path);
        vm.IsModified = true;
        var prompted = false;
        vm.UnsavedChangesRequested += () => prompted = true;

        vm.CloseProjectCommand.Execute(null);

        Assert.True(prompted);
        Assert.True(vm.IsProjectOpen);                    // not closed yet

        vm.DiscardAndProceed();

        Assert.False(vm.IsProjectOpen);
        Assert.Null(AppSettings.LastProjectPath);
    }

    [Fact]
    public async Task Close_Cancelled_KeepsEverything()
    {
        var path = WriteProject("p.dialogproject");
        var vm   = await OpenVmWithCanvasContent(path);
        vm.IsModified = true;

        vm.CloseProjectCommand.Execute(null);
        vm.CancelPendingNavigation();

        Assert.True(vm.IsProjectOpen);
        Assert.Equal(path, AppSettings.LastProjectPath);
        Assert.NotEmpty(vm.Canvas.Nodes);
    }

    [Fact]
    public async Task CanExecute_TracksProjectOpenState()
    {
        var vm = MakeVm();
        Assert.False(vm.CloseProjectCommand.CanExecute(null));

        var path = WriteProject("p.dialogproject");
        await InvokeLoadProjectAsync(vm, path);
        Assert.True(vm.CloseProjectCommand.CanExecute(null));

        vm.CloseProjectCommand.Execute(null);
        Assert.False(vm.CloseProjectCommand.CanExecute(null));
    }

    // ── Branch-switch teardown keeps its distinct semantics ──────────────

    [Fact]
    public async Task ReloadFromDisk_VanishedFile_KeepsLastPathAndCanvas()
    {
        var path = WriteProject("p.dialogproject");
        var vm   = await OpenVmWithCanvasContent(path);

        File.Delete(path);
        vm.ReloadCurrentProjectFromDisk();

        Assert.False(vm.IsProjectOpen);
        Assert.Equal(path, AppSettings.LastProjectPath);   // may reappear on switch back
        Assert.NotEmpty(vm.Canvas.Nodes);                  // canvas deliberately untouched
    }
}
```

- [x] **Step 2: Run to verify red**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelCloseProjectTests"`
Expected: FAIL to compile — `'MainWindowViewModel' does not contain a definition for 'CloseProjectCommand'`.

- [x] **Step 3: Extract `CloseProjectCore` and add the command**

In `DialogEditor.ViewModels\ViewModels\MainWindowViewModel.cs`:

**(a)** Replace the body of `ReloadCurrentProjectFromDisk`'s vanished-file branch (the
block from `SetProject(null);` through the two `NotifyCanExecuteChanged()` calls plus
its `return;`) with:

```csharp
        if (!File.Exists(path))
        {
            AppLog.Info($"Project file not present on current branch: {path}");
            CloseProjectCore(Loc.Format("Status_ProjectNotOnBranch", path));
            return;
        }
```

**(b)** Add below `ReloadCurrentProjectFromDisk`:

```csharp
    /// Shared project-state teardown, used by the Close Project command and by
    /// ReloadCurrentProjectFromDisk when the file vanished on a branch switch.
    /// Deliberately does NOT clear AppSettings.LastProjectPath or the canvas:
    /// on a branch switch the file may reappear on switching back, and that path
    /// keeps the canvas as-is. The user command layers those two on top.
    private void CloseProjectCore(string statusText)
    {
        SetProject(null);
        _projectPath = null;
        Detail.ProjectPath  = null;
        Canvas.ProjectPath  = null;
        OnPropertyChanged(nameof(HasLocalVoFolder));
        BatchImportVoAllCommand.NotifyCanExecuteChanged();   // gate depends on _projectPath
        CurrentProjectName = null;
        IsModified = false;        // nothing open → not dirty
        _attributionPath = null;   // force attribution rebuild next time
        StatusText = statusText;
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
    }
```

**(c)** Add the command next to the New/Open project commands (after `OpenProject`
/ `DoOpenProject`, ~line 532), mirroring their `GuardDirtyThen` shape:

```csharp
    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void CloseProject()
        => GuardDirtyThen(DoCloseProject);

    private void DoCloseProject()
    {
        var name = CurrentProjectName ?? "?";
        AppLog.Info($"Closed project: {_projectPath}");
        CloseProjectCore(Loc.Format("Status_ProjectClosed", name));
        // User-intent extras the branch-switch teardown must not do: the canvas may
        // show patched content that exists nowhere after close, and a deliberate
        // close should stick across restarts (no auto-reopen of this project).
        Canvas.Clear();
        Detail.Clear();
        CurrentConversationName = null;
        _currentFile = null;
        OnPropertyChanged(nameof(CanValidateVO));
        AppSettings.LastProjectPath = null;
    }
```

**(d)** In `SetProject`, add one line to the `NotifyCanExecuteChanged` block (after
`SaveProjectAsCommand.NotifyCanExecuteChanged();`):

```csharp
        CloseProjectCommand.NotifyCanExecuteChanged();
```

- [x] **Step 4: Run the new tests, then the full suite**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelCloseProjectTests"`
Expected: PASS (5).

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (1901).

- [x] **Step 5: Commit**

```powershell
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelCloseProjectTests.cs
git commit -m @'
feat(project): add Close Project command with shared teardown core

CloseProjectCore is extracted from ReloadCurrentProjectFromDisk so both
closes share one definition; the user command adds the dirty guard,
canvas clear, and LastProjectPath clear on top. Branch-switch semantics
(keep LastProjectPath, keep canvas) are regression-tested.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 3: Menu item, strings, Ctrl+W (UI wiring)

**Files:**
- Modify: `DialogEditor.Avalonia\Resources\Strings.axaml` (menu strings ~line 883, status strings ~line 782)
- Modify: `DialogEditor.Avalonia\Views\MainWindow.axaml` (File menu, after Merge Projects ~line 58-61)
- Modify: `DialogEditor.Avalonia\Views\MainWindow.axaml.cs` (`OnKeyDownTunnel` ~line 247)
- Modify: `docs\superpowers\specs\2026-07-05-close-project-design.md` (one-line amendment)

**Interfaces:**
- Consumes: `CloseProjectCommand` (Task 2).
- Produces: nothing downstream — this completes the feature.

No new unit tests: this task is pure XAML/wiring with no testable logic (the
command's behaviour is covered by Task 2; shortcut dispatch lives in the
untestable `OnKeyDownTunnel` switch alongside every other app shortcut).

- [x] **Step 1: Add the three strings**

In `DialogEditor.Avalonia\Resources\Strings.axaml`, after `ToolTip_MergeProjects`
(~line 884):

```xml
    <sys:String x:Key="Menu_CloseProject">Close Project</sys:String>
    <sys:String x:Key="ToolTip_CloseProject">Close the current project and return to browse mode. Game conversations stay browsable read-only. If the canvas has unsaved edits you will be asked to save or discard them first. The project will not reopen automatically on next launch; use File > Open Project to return to it.</sys:String>
```

After `Status_ProjectNew` (~line 782):

```xml
    <sys:String x:Key="Status_ProjectClosed">Closed project '{0}'</sys:String>
```

- [x] **Step 2: Add the menu item**

In `DialogEditor.Avalonia\Views\MainWindow.axaml`, directly after the Merge Projects
`MenuItem` (before the first `<Separator/>`):

```xml
                        <MenuItem Header="{DynamicResource Menu_CloseProject}"
                                  Command="{Binding CloseProjectCommand}"
                                  InputGesture="Ctrl+W"
                                  ToolTip.Tip="{DynamicResource ToolTip_CloseProject}"
                                  AutomationProperties.HelpText="{DynamicResource ToolTip_CloseProject}"/>
```

- [x] **Step 3: Add the Ctrl+W case to `OnKeyDownTunnel`**

In `DialogEditor.Avalonia\Views\MainWindow.axaml.cs`, add a case to the switch in
`OnKeyDownTunnel` (next to the other Ctrl single-letter cases). The explicit
`CanExecute` check matters: `RelayCommand.Execute` does not gate itself, and with no
project open Ctrl+W must not clear a remembered `LastProjectPath`:

```csharp
            case Key.W when e.KeyModifiers == KeyModifiers.Control:
                if (vm.CloseProjectCommand.CanExecute(null))
                    vm.CloseProjectCommand.Execute(null);
                e.Handled = true;
                break;
```

- [x] **Step 4: Amend the spec's shortcut mechanism line**

In `docs\superpowers\specs\2026-07-05-close-project-design.md`, in the Components
table, replace the `MainWindow.axaml(.cs)` row's description:

Old: `Window-level KeyBinding for Ctrl+W → CloseProjectCommand (InputGesture on MenuItem is display-only in Avalonia).`

New: `Ctrl+W case in the OnKeyDownTunnel switch → CloseProjectCommand, gated by CanExecute (the codebase dispatches all shortcuts there; InputGesture on MenuItem is display-only in Avalonia).`

- [x] **Step 5: Build and run the full suite**

Run: `dotnet build "DialogEditor.slnx"`
Expected: Build succeeded (same pre-existing warnings only).

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (1901).

- [x] **Step 6: Commit**

```powershell
git add DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.Avalonia/Views/MainWindow.axaml.cs docs/superpowers/specs/2026-07-05-close-project-design.md
git commit -m @'
feat(ui): wire Close Project into File menu with Ctrl+W

Menu item after Merge Projects, localised strings, tooltip per UI
guidelines, and an OnKeyDownTunnel case gated by CanExecute so Ctrl+W
with no project open cannot clear a remembered LastProjectPath. Spec
amended: shortcut dispatch is the tunnel handler, not a KeyBinding.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 4: Close the gap entry

**Files:**
- Modify: `Gaps.md` (the `### No "Close Project" command` entry)

**Interfaces:** none — documentation only.

- [x] **Step 1: Mark the gap implemented**

Replace the entry body with:

```markdown
### ~~No "Close Project" command~~ ✓ Implemented (2026-07-05)
**File ▸ Close Project** (Ctrl+W, enabled only with a project open) runs the existing
unsaved-changes guard, then tears down project state via `CloseProjectCore` (shared
with the branch-switch vanished-file path, which keeps its distinct semantics: no
`LastProjectPath` clear, no canvas clear), clears the canvas (`ConversationViewModel.Clear()`),
and clears `AppSettings.LastProjectPath` so the next launch starts projectless.
Spec: docs/superpowers/specs/2026-07-05-close-project-design.md.
```

- [x] **Step 2: Full suite**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (1901).

- [x] **Step 3: Commit**

```powershell
git add Gaps.md
git commit -m @'
docs(gaps): mark close-project gap implemented

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```
