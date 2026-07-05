# Save Project As Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `File ▸ Save Project As…` (Ctrl+Shift+S) with classic rebind semantics: the editor switches to the new file, the internal project `Name` follows the new filename, and the `_vo/` sidecar folder is copied alongside.

**Architecture:** A new `SaveProjectAsCommand` on `MainWindowViewModel` reuses the existing `IFilePicker.PickSaveFileAsync` seam (same call shape as `DoNewProject`) and a fold-canvas helper extracted from `SaveProject()`. UI wiring follows the codebase's established pattern: display-only `InputGesture` on the `MenuItem` plus a real key case in `MainWindow.OnKeyDownTunnel`.

**Tech Stack:** C# / .NET, Avalonia, CommunityToolkit.Mvvm (`[RelayCommand]`), xUnit.

**Spec:** `docs/superpowers/specs/2026-07-05-save-project-as-design.md`

## Global Constraints

- Strict red/green TDD — failing test before implementation (CLAUDE.md).
- No user-visible text hard-coded — all strings in `Strings.axaml` (CLAUDE.md).
- Every caught exception logged via `AppLog.Error(...)`/`AppLog.Warn(...)`; `OperationCanceledException` swallowed silently; no bare `catch { }` (CLAUDE.md).
- New menu item carries `ToolTip.Tip` **and** `AutomationProperties.HelpText` (CLAUDE.md + `AutomationHelpTextTests`).
- `DialogEditor.Tests` runs serially — do not change test parallelisation.
- Rebind ordering (spec): `Name` renames pre-write (the file must carry the new name); `_projectPath` rebinds only after a successful write; `Name` reverts on failure.
- A `_vo/` copy failure must NOT roll back the project save — log + distinct status message.
- `CanSaveProjectAs` requires only an open project (`_project`/`_projectPath` non-null), **not** `IsModified`.

---

### Task 1: Extract `FoldCanvasIntoProject()` from `SaveProject()` (refactor under green)

**Files:**
- Modify: `DialogEditor.ViewModels\ViewModels\MainWindowViewModel.cs:897-948`

**Interfaces:**
- Consumes: existing `SaveProject()` body.
- Produces: `private void FoldCanvasIntoProject()` — folds open-canvas edits into `_project` via `SetProject(...)`; no-op when no conversation is open. Task 2 calls it.

- [ ] **Step 1: Confirm the suite is green before refactoring**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (all tests).

- [ ] **Step 2: Extract the fold block**

In `MainWindowViewModel.cs`, replace `SaveProject()` (lines 897–945) with:

```csharp
    [RelayCommand(CanExecute = nameof(CanSaveProject))]
    private void SaveProject()
    {
        if (_project is null || _projectPath is null) return;
        try
        {
            FoldCanvasIntoProject();
            DialogProjectSerializer.SaveToFile(_projectPath, _project!);
            Canvas.IsModified = false;
            IsModified = false;
            SaveCommand.NotifyCanExecuteChanged();
            SaveProjectCommand.NotifyCanExecuteChanged();
            AppLog.Info($"Project saved: {_projectPath}");
            StatusText = Loc.Format("Status_ProjectSaved", _project.Name);
            ConversationSaved?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to save project", ex);
            StatusText = Loc.Format("Status_SaveError", _project?.Name ?? "?", ex.Message);
        }
    }

    /// With a conversation open, folds the canvas edits into its patch on _project.
    /// With no conversation open (e.g. a freshly conflict-merged project), the
    /// in-memory _project is already complete — no-op.
    private void FoldCanvasIntoProject()
    {
        if (_currentFile is null || Canvas.BaseSnapshot is null) return;

        var patch  = DiffEngine.Diff(_currentFile.Name, Canvas.BaseSnapshot, Canvas.BuildSnapshot(), _provider!.Language);
        patch = patch with { NodeComments = Canvas.NodeComments };

        // WithPatch replaces the stored patch wholesale, but the diff only knows
        // the canvas language — carry over imported translations for every other
        // language, or they would be silently erased on each save. The current
        // language always takes the freshly diffed value (including "no entry"
        // when the text was reverted to vanilla).
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
        var layout      = Canvas.GetCurrentLayout();
        var annotations = Canvas.GetCurrentAnnotations();
        SetProject(_project!.WithPatch(patch).WithLayout(_currentFile.Name, layout)
            .WithAnnotations(_currentFile.Name, annotations));
    }
```

The comment block and fold logic are moved **verbatim** — only the `if` guard inverts (early-return instead of wrapping).

- [ ] **Step 3: Verify still green**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS — behaviour unchanged (persistence tests in `MainWindowViewModelPersistenceTests` cover the fold path).

- [ ] **Step 4: Commit**

```powershell
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git commit -m @'
refactor(save): extract FoldCanvasIntoProject from SaveProject

Preparation for Save Project As, which needs the same fold without
duplicating it. No behaviour change.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 2: `SaveProjectAsCommand` — core rebind behaviour (TDD)

**Files:**
- Create: `DialogEditor.Tests\ViewModels\MainWindowViewModelSaveAsTests.cs`
- Modify: `DialogEditor.ViewModels\ViewModels\MainWindowViewModel.cs` (new command below `CanSaveProject()`, ~line 948)
- Modify: `DialogEditor.Avalonia\Resources\Strings.axaml` (new keys near `Menu_SaveProject` line 734 and `Dialog_NewProject` line 871)

**Interfaces:**
- Consumes: `FoldCanvasIntoProject()` (Task 1); `IFilePicker.PickSaveFileAsync(string title, string suggestedName, string extension, string extensionDescription)` (`DialogEditor.ViewModels\Services\IFilePicker.cs:15`); `StubFilePicker(saveResult: …)` (`DialogEditor.Tests\Helpers\StubFilePicker.cs`).
- Produces: `SaveProjectAsCommand` (generated `IAsyncRelayCommand`), `private bool CanSaveProjectAs()`. Task 3 extends the command with the `_vo/` copy; Task 4 binds the command in XAML/key handler.

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests\ViewModels\MainWindowViewModelSaveAsTests.cs`:

```csharp
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Save Project As: classic rebind semantics — the editor switches to the new
/// file, the internal Name follows the new filename, the original is untouched.
/// Spec: docs/superpowers/specs/2026-07-05-save-project-as-design.md
public class MainWindowViewModelSaveAsTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _tempDir;

    public MainWindowViewModelSaveAsTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_saveas_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _tempDir = Path.Combine(Path.GetTempPath(), $"saveas_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_tempDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static MainWindowViewModel MakeVm(string? saveResult = null) =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker(saveResult: saveResult));

    private static Task InvokeLoadProjectAsync(MainWindowViewModel vm, string path)
    {
        var mi = typeof(MainWindowViewModel).GetMethod("LoadProjectAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Task)mi.Invoke(vm, [path, false])!;
    }

    private static void InjectProject(MainWindowViewModel vm, DialogProject project)
    {
        var mi = typeof(MainWindowViewModel).GetMethod("SetProject",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        mi.Invoke(vm, [project]);
    }

    /// Writes an empty project named after the file into _tempDir; returns its path.
    private string WriteProject(string relativePath)
    {
        var path = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        DialogProjectSerializer.SaveToFile(
            path, DialogProject.Empty(Path.GetFileNameWithoutExtension(path)));
        return path;
    }

    /// VM with the given project opened from disk and the picker primed to answer saveAsTarget.
    private async Task<MainWindowViewModel> OpenVm(string projectPath, string? saveAsTarget)
    {
        var vm = MakeVm(saveResult: saveAsTarget);
        await InvokeLoadProjectAsync(vm, projectPath);
        return vm;
    }

    // ── Core rebind behaviour ─────────────────────────────────────────────

    [Fact]
    public async Task SaveAs_WritesNewFile_RenamesProject_AndRebinds()
    {
        var orig    = WriteProject("orig.dialogproject");
        var target  = Path.Combine(_tempDir, "fork", "forked.dialogproject");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var vm      = await OpenVm(orig, target);

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.True(File.Exists(target), "Save As should write the new file.");
        Assert.Equal("forked", DialogProjectSerializer.LoadFromFile(target).Name);
        Assert.Equal("orig", DialogProjectSerializer.LoadFromFile(orig).Name);   // original untouched
        Assert.Equal(target, AppSettings.LastProjectPath);
        Assert.Equal("forked", vm.CurrentProjectName);
        Assert.False(vm.IsModified);
    }

    [Fact]
    public async Task SaveAs_SubsequentSave_TargetsNewPath()
    {
        var orig   = WriteProject("orig.dialogproject");
        var target = Path.Combine(_tempDir, "forked.dialogproject");
        var vm     = await OpenVm(orig, target);

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        // A later edit + plain Save must land in the NEW file only.
        InjectProject(vm, DialogProjectSerializer.LoadFromFile(target).WithNewConversation("extra"));
        vm.IsModified = true;
        vm.SaveProjectCommand.Execute(null);

        Assert.True(DialogProjectSerializer.LoadFromFile(target).IsNewConversation("extra"));
        Assert.False(DialogProjectSerializer.LoadFromFile(orig).IsNewConversation("extra"));
    }

    [Fact]
    public async Task SaveAs_Cancelled_IsNoOp()
    {
        var orig = WriteProject("orig.dialogproject");
        var vm   = await OpenVm(orig, saveAsTarget: null);   // picker cancels

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.Equal(orig, AppSettings.LastProjectPath);
        Assert.Equal("orig", vm.CurrentProjectName);
        Assert.Single(Directory.GetFiles(_tempDir, "*.dialogproject"));
    }

    [Fact]
    public async Task SaveAs_SamePathChosen_BehavesAsPlainSave()
    {
        var orig = WriteProject("orig.dialogproject");
        var vm   = await OpenVm(orig, saveAsTarget: orig);
        vm.IsModified = true;

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.Equal("orig", DialogProjectSerializer.LoadFromFile(orig).Name);  // no rename
        Assert.Equal(orig, AppSettings.LastProjectPath);
        Assert.False(vm.IsModified);
    }

    [Fact]
    public async Task SaveAs_CleanProject_IsExecutable_WhilePlainSaveIsNot()
    {
        var orig = WriteProject("orig.dialogproject");
        var vm   = await OpenVm(orig, Path.Combine(_tempDir, "copy.dialogproject"));

        // Freshly loaded project: not modified.
        Assert.False(vm.IsModified);
        Assert.False(vm.SaveProjectCommand.CanExecute(null));
        Assert.True(vm.SaveProjectAsCommand.CanExecute(null),
            "Save As must not require IsModified — forking a clean project is legitimate.");
    }

    [Fact]
    public void SaveAs_NoProjectOpen_IsNotExecutable()
    {
        var vm = MakeVm();
        Assert.False(vm.SaveProjectAsCommand.CanExecute(null));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelSaveAsTests"`
Expected: FAIL to **compile** — `SaveProjectAsCommand` does not exist. (A compile error is the red state here.)

- [ ] **Step 3: Add the strings**

In `DialogEditor.Avalonia\Resources\Strings.axaml`, after `Menu_SaveProject` (line 734):

```xml
    <sys:String x:Key="Menu_SaveProjectAs">Save Project As…</sys:String>
    <sys:String x:Key="ToolTip_SaveProjectAs">Save the open project under a new name or location. The editor switches to the new file — later saves go there — and any voice-over files (the _vo folder) are copied alongside it. The original file is left unchanged.</sys:String>
```

After `Dialog_NewProjectDefault` (line 872):

```xml
    <sys:String x:Key="Dialog_SaveProjectAs">Save project as</sys:String>
```

After `Status_ProjectSaved` (line 786):

```xml
    <sys:String x:Key="Status_ProjectSavedAs">Project saved as: {0}</sys:String>
    <sys:String x:Key="Status_SaveAsVoCopyFailed">Project saved as: {0} — but copying the voice-over (_vo) folder failed: {1}</sys:String>
```

(`Status_SaveAsVoCopyFailed` is consumed in Task 3 but added here so the strings land in one commit.)

- [ ] **Step 4: Implement the command**

In `MainWindowViewModel.cs`, directly after `CanSaveProject()` (~line 948):

```csharp
    // ── Save As — rebind to a new file (spec: 2026-07-05-save-project-as) ──
    [RelayCommand(CanExecute = nameof(CanSaveProjectAs))]
    private async Task SaveProjectAs()
    {
        if (_project is null || _projectPath is null) return;

        var path = await _filePicker.PickSaveFileAsync(
            Loc.Get("Dialog_SaveProjectAs"),
            Path.GetFileNameWithoutExtension(_projectPath),
            ".dialogproject",
            Loc.Get("FileType_DialogProject"));
        if (path is null) return;

        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_projectPath),
                StringComparison.OrdinalIgnoreCase))
        {
            SaveProject();   // same file — a plain save; no rename, no rebind
            return;
        }

        var oldPath = _projectPath;
        var oldName = _project.Name;
        try
        {
            FoldCanvasIntoProject();
            // Rename before writing so the file carries the new name; rebind the
            // path only after the write succeeds so a failed save leaves the
            // editor bound to the original file.
            SetProject(_project! with { Name = Path.GetFileNameWithoutExtension(path) });
            DialogProjectSerializer.SaveToFile(path, _project!);
        }
        catch (Exception ex)
        {
            SetProject(_project! with { Name = oldName });
            AppLog.Error($"Failed to save project as '{path}'", ex);
            StatusText = Loc.Format("Status_SaveError", oldName, ex.Message);
            return;
        }

        _projectPath        = path;
        Detail.ProjectPath  = path;
        Canvas.ProjectPath  = path;

        Canvas.IsModified = false;
        IsModified = false;
        SaveCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasLocalVoFolder));
        BatchImportVoAllCommand.NotifyCanExecuteChanged();
        AppSettings.LastProjectPath = path;
        CurrentProjectName = _project!.Name;
        AppLog.Info($"Project saved as: {path}");
        StatusText = Loc.Format("Status_ProjectSavedAs", _project!.Name);
        ConversationSaved?.Invoke();
    }

    private bool CanSaveProjectAs() =>
        _project is not null && _projectPath is not null;
```

Then add `SaveProjectAsCommand.NotifyCanExecuteChanged();` immediately after **every** existing `SaveProjectCommand.NotifyCanExecuteChanged();` call (5 sites — lines 243, 327, 552, 663, and the one inside `SaveProject()`; find them with `Grep "SaveProjectCommand.NotifyCanExecuteChanged"`).

- [ ] **Step 5: Run the Save As tests**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelSaveAsTests"`
Expected: PASS (all 6).

- [ ] **Step 6: Run the full suite**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS — note `NoStrayHexTests`/`AutomationHelpTextTests` etc. are structural scans; the new strings/commands must not trip them (no hex, no hard-coded UI text).

- [ ] **Step 7: Commit**

```powershell
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/ViewModels/MainWindowViewModelSaveAsTests.cs
git commit -m @'
feat(save): Save Project As command with rebind semantics

Classic Save As: picker (current filename suggested), internal Name
follows the new filename, path rebinds only after a successful write,
original file untouched. No IsModified gate - forking a clean project
is legitimate.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 3: `_vo/` sidecar copy (TDD)

**Files:**
- Modify: `DialogEditor.Tests\ViewModels\MainWindowViewModelSaveAsTests.cs` (append tests)
- Modify: `DialogEditor.ViewModels\ViewModels\MainWindowViewModel.cs` (extend `SaveProjectAs`, add helpers)

**Interfaces:**
- Consumes: `SaveProjectAs()` from Task 2.
- Produces: `private static string? CopyVoFolder(string oldPath, string newPath)` — returns `null` on success/nothing-to-copy, else the failure message. Used only by `SaveProjectAs`.

- [ ] **Step 1: Write the failing tests**

Append to `MainWindowViewModelSaveAsTests.cs` (inside the class):

```csharp
    // ── _vo/ sidecar copy ─────────────────────────────────────────────────

    [Fact]
    public async Task SaveAs_DifferentDirectory_CopiesVoFolderRecursively()
    {
        var orig = WriteProject("orig.dialogproject");
        var voFile = Path.Combine(_tempDir, "_vo", "speaker", "conv_12.wem");
        Directory.CreateDirectory(Path.GetDirectoryName(voFile)!);
        File.WriteAllBytes(voFile, [1, 2, 3]);

        var target = Path.Combine(_tempDir, "fork", "forked.dialogproject");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var vm = await OpenVm(orig, target);

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        var copied = Path.Combine(_tempDir, "fork", "_vo", "speaker", "conv_12.wem");
        Assert.True(File.Exists(copied), "_vo/ must be copied next to the new project file.");
        Assert.True(File.Exists(voFile), "the original _vo/ must be left in place.");
    }

    [Fact]
    public async Task SaveAs_NoVoFolder_CopiesNothing()
    {
        var orig   = WriteProject("orig.dialogproject");
        var target = Path.Combine(_tempDir, "fork", "forked.dialogproject");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var vm = await OpenVm(orig, target);

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.False(Directory.Exists(Path.Combine(_tempDir, "fork", "_vo")));
    }

    [Fact]
    public async Task SaveAs_VoCopyFailure_ProjectIsStillSavedAndRebound()
    {
        var orig = WriteProject("orig.dialogproject");
        var voFile = Path.Combine(_tempDir, "_vo", "a.wem");
        Directory.CreateDirectory(Path.GetDirectoryName(voFile)!);
        File.WriteAllBytes(voFile, [1]);

        var forkDir = Path.Combine(_tempDir, "fork");
        Directory.CreateDirectory(forkDir);
        // A FILE named "_vo" blocks Directory.CreateDirectory → deterministic copy failure.
        File.WriteAllText(Path.Combine(forkDir, "_vo"), "blocker");

        var target = Path.Combine(forkDir, "forked.dialogproject");
        var vm = await OpenVm(orig, target);

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.True(File.Exists(target), "the save itself must not be rolled back.");
        Assert.Equal(target, AppSettings.LastProjectPath);
        Assert.False(vm.IsModified);
    }
```

- [ ] **Step 2: Run tests to verify the copy tests fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelSaveAsTests"`
Expected: `SaveAs_DifferentDirectory_CopiesVoFolderRecursively` FAILS (no copy happens yet); `SaveAs_NoVoFolder_CopiesNothing` and `SaveAs_VoCopyFailure_*` may already pass — that's fine, the copy test is the red driver.

- [ ] **Step 3: Implement the copy**

In `SaveProjectAs()` (Task 2), insert **after** the three rebind lines (`_projectPath`/`Detail.ProjectPath`/`Canvas.ProjectPath`) and **before** `Canvas.IsModified = false;`:

```csharp
        var voCopyError = CopyVoFolder(oldPath, path);
```

Replace the `StatusText` line at the end of `SaveProjectAs()` with:

```csharp
        StatusText = voCopyError is null
            ? Loc.Format("Status_ProjectSavedAs", _project!.Name)
            : Loc.Format("Status_SaveAsVoCopyFailed", _project!.Name, voCopyError);
```

Add the helpers after `CanSaveProjectAs()`:

```csharp
    /// Copies the _vo/ sidecar folder next to the new project file when the
    /// directory changed (same directory → the folder is already adjacent).
    /// Returns null on success or nothing-to-copy, else the failure message —
    /// a failed copy must not roll back the already-written project file.
    private static string? CopyVoFolder(string oldPath, string newPath)
    {
        try
        {
            var oldDir = Path.GetDirectoryName(oldPath)!;
            var newDir = Path.GetDirectoryName(newPath)!;
            if (string.Equals(Path.GetFullPath(oldDir), Path.GetFullPath(newDir),
                    StringComparison.OrdinalIgnoreCase))
                return null;
            var source = Path.Combine(oldDir, "_vo");
            if (!Directory.Exists(source)) return null;
            CopyDirectoryRecursive(source, Path.Combine(newDir, "_vo"));
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Error("Save As: copying the _vo/ sidecar folder failed", ex);
            return ex.Message;
        }
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectoryRecursive(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
```

- [ ] **Step 4: Run the Save As tests**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelSaveAsTests"`
Expected: PASS (all 9).

- [ ] **Step 5: Commit**

```powershell
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelSaveAsTests.cs
git commit -m @'
feat(save): copy _vo/ sidecar alongside on Save As

The saved-as project keeps working VO (playback, validation, F5 sync,
bundle export) without manual folder surgery. Copy failure is logged
and reported in the status bar but never rolls back the save.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 4: UI wiring — menu item and Ctrl+Shift+S

**Files:**
- Modify: `DialogEditor.Avalonia\Views\MainWindow.axaml:50-52` (menu item after Save Project)
- Modify: `DialogEditor.Avalonia\Views\MainWindow.axaml.cs:313-318` (key case in `OnKeyDownTunnel`)

**Interfaces:**
- Consumes: `SaveProjectAsCommand` (Task 2), string keys `Menu_SaveProjectAs`/`ToolTip_SaveProjectAs` (Task 2).
- Produces: user-facing menu entry + shortcut. Nothing downstream consumes this.

- [ ] **Step 1: Add the menu item**

In `MainWindow.axaml`, directly after the Save Project `MenuItem` (line 52):

```xml
                        <MenuItem Header="{DynamicResource Menu_SaveProjectAs}"
                                  Command="{Binding SaveProjectAsCommand}"
                                  InputGesture="Ctrl+Shift+S"
                                  ToolTip.Tip="{DynamicResource ToolTip_SaveProjectAs}"
                                  AutomationProperties.HelpText="{DynamicResource ToolTip_SaveProjectAs}"/>
```

(`InputGesture` is display-only in Avalonia — the real binding is Step 2. `ToolTip.Tip` + `HelpText` mirroring is mandatory per CLAUDE.md and enforced by `AutomationHelpTextTests`.)

- [ ] **Step 2: Add the key case**

In `MainWindow.axaml.cs`'s `OnKeyDownTunnel` switch, directly **before** the existing `case Key.S when e.KeyModifiers == KeyModifiers.Control:` (line 313 — exact-equality matching means order is not load-bearing, but keep the more specific chord first for readability):

```csharp
            case Key.S when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
                    CanvasView.FocusEditor();   // commit a focused TextBox edit first, like Ctrl+S
                vm.SaveProjectAsCommand.Execute(null);
                e.Handled = true;
                break;
```

- [ ] **Step 3: Run the full suite (structural enforcers cover the new XAML)**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS — `AutomationHelpTextTests`, `NoStaticStringResourceTests`, `NoStrayHexTests` all scan `MainWindow.axaml` and must stay green.

- [ ] **Step 4: Build and launch the app for a manual smoke check**

Run: `dotnet build DialogEditor.slnx`
Expected: Build succeeded.

Manual check (or via the `verify`/`run` skill): open a project → File menu shows "Save Project As…" with Ctrl+Shift+S; the item is disabled with no project open; Ctrl+Shift+S opens the save picker; saving to a new folder rebinds the window title and copies `_vo/` if present.

- [ ] **Step 5: Commit**

```powershell
git add DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.Avalonia/Views/MainWindow.axaml.cs
git commit -m @'
feat(save): wire Save Project As menu item and Ctrl+Shift+S

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 5: Close the gap entry

**Files:**
- Modify: `Gaps.md` (the `### No "Save As…"` entry under Feature Gaps)

**Interfaces:** none — documentation only.

- [ ] **Step 1: Mark the gap implemented**

Replace the `### No "Save As…"` entry body in `Gaps.md` with:

```markdown
### ~~No "Save As…"~~ ✓ Implemented (2026-07-05)
**File ▸ Save Project As…** (Ctrl+Shift+S) saves the open project under a new
name/location with classic rebind semantics: subsequent saves target the new file, the
internal project `Name` follows the new filename, the window title/`LastProjectPath`
update, and the `_vo/` sidecar folder is copied alongside when the directory changes
(copy failure is reported but never rolls back the save). The command is available
whenever a project is open — no dirty-state requirement, so forking a clean project
works. Spec: docs/superpowers/specs/2026-07-05-save-project-as-design.md.
```

- [ ] **Step 2: Full suite one last time**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS.

- [ ] **Step 3: Commit**

```powershell
git add Gaps.md
git commit -m @'
docs(gaps): mark Save As gap implemented

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```
