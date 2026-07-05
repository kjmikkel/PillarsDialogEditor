# Save-Error Visibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface caught save exceptions in the existing `ExceptionReportWindow` instead of status-bar text only.

**Architecture:** A nullable `ReportSaveError` delegate on `MainWindowViewModel` (same pattern as `ShowImportWarnings`) is invoked from the three save-failure sites; `MainWindow` wires it to `App.ShowExceptionReport` (made public), which already handles per-type dedupe and window creation. Status-bar messages are unchanged.

**Tech Stack:** C# / .NET, Avalonia, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-05-save-error-visibility-design.md`

## Global Constraints

- Strict red/green TDD — failing test before implementation (CLAUDE.md).
- Every caught exception logged via `AppLog.Error(...)`/`AppLog.Warn(...)`; no bare `catch { }` (CLAUDE.md) — this plan *removes* one existing violation.
- No user-visible text hard-coded (no new strings are needed; status messages unchanged).
- Status-bar behaviour must remain byte-identical; the window is additive.
- Unwired delegate (tests, PatchManager host) must be a silent no-op.
- A `_vo/` copy failure still must NOT roll back the project save.

---

### Task 1: `ReportSaveError` delegate + three call sites (TDD)

**Files:**
- Modify: `DialogEditor.Tests\ViewModels\MainWindowViewModelSaveAsTests.cs` (append tests)
- Modify: `DialogEditor.ViewModels\ViewModels\MainWindowViewModel.cs` (delegate near `ShowImportWarnings` ~line 92; `SaveProject` catch; `SaveProjectAs` catch + VO-copy handling; `CopyVoFolder` return type)

**Interfaces:**
- Consumes: existing `SaveProject()`, `SaveProjectAs()`, `CopyVoFolder(string, string)` (currently `string?`-returning), and the `MainWindowViewModelSaveAsTests` helpers (`WriteProject`, `OpenVm`, `MakeVm`, `InvokeLoadProjectAsync`).
- Produces: `public Action<Exception>? ReportSaveError { get; set; }` on `MainWindowViewModel`; `CopyVoFolder` now returns `Exception?`. Task 2 wires the delegate in the UI layer.

- [x] **Step 1: Write the failing tests**

Append inside the class in `DialogEditor.Tests\ViewModels\MainWindowViewModelSaveAsTests.cs`:

```csharp
    // ── ReportSaveError delegate (save-error visibility spec) ─────────────

    /// Returns a Save As target whose parent "directory" is actually a file,
    /// so DialogProjectSerializer.SaveToFile deterministically throws.
    private string BlockedSaveTarget()
    {
        var blocker = Path.Combine(_tempDir, "blocked");
        File.WriteAllText(blocker, "not a directory");
        return Path.Combine(blocker, "forked.dialogproject");
    }

    [Fact]
    public async Task SaveAs_WriteFailure_InvokesReportSaveError_AndStaysBoundToOriginal()
    {
        var orig = WriteProject("orig.dialogproject");
        var vm   = await OpenVm(orig, BlockedSaveTarget());
        Exception? reported = null;
        vm.ReportSaveError = ex => reported = ex;

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.NotNull(reported);
        Assert.Equal(orig, AppSettings.LastProjectPath);   // rebind must not have happened
        Assert.Equal("orig", vm.CurrentProjectName);
    }

    [Fact]
    public async Task SaveAs_WriteFailure_NullDelegate_DoesNotThrow()
    {
        var orig = WriteProject("orig.dialogproject");
        var vm   = await OpenVm(orig, BlockedSaveTarget());
        // ReportSaveError deliberately left null.

        await vm.SaveProjectAsCommand.ExecuteAsync(null);   // must not throw

        Assert.Equal(orig, AppSettings.LastProjectPath);
    }

    [Fact]
    public async Task SaveAs_Success_DoesNotInvokeReportSaveError()
    {
        var orig = WriteProject("orig.dialogproject");
        var vm   = await OpenVm(orig, Path.Combine(_tempDir, "forked.dialogproject"));
        Exception? reported = null;
        vm.ReportSaveError = ex => reported = ex;

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.Null(reported);
    }

    [Fact]
    public async Task SaveAs_VoCopyFailure_InvokesReportSaveError_AndStillSaves()
    {
        var orig = WriteProject("orig.dialogproject");
        var voFile = Path.Combine(_tempDir, "_vo", "a.wem");
        Directory.CreateDirectory(Path.GetDirectoryName(voFile)!);
        File.WriteAllBytes(voFile, [1]);

        var forkDir = Path.Combine(_tempDir, "fork");
        Directory.CreateDirectory(forkDir);
        File.WriteAllText(Path.Combine(forkDir, "_vo"), "blocker");   // file blocks dir creation

        var target = Path.Combine(forkDir, "forked.dialogproject");
        var vm = await OpenVm(orig, target);
        Exception? reported = null;
        vm.ReportSaveError = ex => reported = ex;

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.NotNull(reported);                          // partial failure surfaced
        Assert.True(File.Exists(target), "the save itself must not be rolled back.");
    }

    [Fact]
    public async Task PlainSave_WriteFailure_InvokesReportSaveError()
    {
        var orig = WriteProject("orig.dialogproject");
        var vm   = await OpenVm(orig, saveAsTarget: null);
        Exception? reported = null;
        vm.ReportSaveError = ex => reported = ex;

        // Make the open project file read-only so SaveToFile throws.
        File.SetAttributes(orig, FileAttributes.ReadOnly);
        try
        {
            vm.IsModified = true;
            vm.SaveProjectCommand.Execute(null);
            Assert.NotNull(reported);
        }
        finally
        {
            File.SetAttributes(orig, FileAttributes.Normal);   // or Dispose can't delete _tempDir
        }
    }
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelSaveAsTests"`
Expected: FAIL to compile — `ReportSaveError` does not exist (compile error is the red state).

- [x] **Step 3: Implement**

(a) In `DialogEditor.ViewModels\ViewModels\MainWindowViewModel.cs`, next to the other UI-layer delegates (after `ShowImportWarnings`, ~line 92):

```csharp
    /// Set by the UI layer to surface a caught save exception in the exception
    /// report window (status-bar text alone is too easy to miss for a failed save).
    public Action<Exception>? ReportSaveError { get; set; }
```

(b) In `SaveProject()`'s catch block, after the `StatusText` line:

```csharp
        catch (Exception ex)
        {
            AppLog.Error($"Failed to save project", ex);
            StatusText = Loc.Format("Status_SaveError", _project?.Name ?? "?", ex.Message);
            ReportSaveError?.Invoke(ex);
        }
```

(c) In `SaveProjectAs()`'s catch block, after the `StatusText` line (before the `return`):

```csharp
        catch (Exception ex)
        {
            SetProject(_project! with { Name = oldName });
            AppLog.Error($"Failed to save project as '{path}'", ex);
            StatusText = Loc.Format("Status_SaveError", oldName, ex.Message);
            ReportSaveError?.Invoke(ex);
            return;
        }
```

(d) Change `CopyVoFolder` to return the exception instead of its message:

```csharp
    /// Copies the _vo/ sidecar folder next to the new project file when the
    /// directory changed (same directory → the folder is already adjacent).
    /// Returns null on success or nothing-to-copy, else the exception —
    /// a failed copy must not roll back the already-written project file.
    private static Exception? CopyVoFolder(string oldPath, string newPath)
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
            return ex;
        }
    }
```

(e) In `SaveProjectAs()`, surface the VO-copy failure and use `.Message` in the status text:

```csharp
        var voCopyError = CopyVoFolder(oldPath, path);
        if (voCopyError is not null)
            ReportSaveError?.Invoke(voCopyError);
```

and at the end of the method:

```csharp
        StatusText = voCopyError is null
            ? Loc.Format("Status_ProjectSavedAs", _project!.Name)
            : Loc.Format("Status_SaveAsVoCopyFailed", _project!.Name, voCopyError.Message);
```

- [x] **Step 4: Run the Save As tests, then the full suite**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelSaveAsTests"`
Expected: PASS (14).

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (all).

- [x] **Step 5: Commit**

```powershell
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelSaveAsTests.cs
git commit -m @'
feat(save): ReportSaveError delegate surfaces caught save exceptions

Plain save, Save As, and the Save As _vo/ copy failure now invoke a
nullable UI-layer delegate with the caught exception. Status-bar text
unchanged; unwired delegate is a no-op.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 2: UI wiring + bare-catch cleanup

**Files:**
- Modify: `DialogEditor.Avalonia\App.axaml.cs:92` (`private` → `public` on `ShowExceptionReport`)
- Modify: `DialogEditor.Avalonia\Views\MainWindow.axaml.cs` (wiring, after the `ShowImportWarnings` block at lines 85–89)
- Modify: `DialogEditor.Avalonia\Views\ExceptionReportWindow.axaml.cs:26-31` (bare catch)

**Interfaces:**
- Consumes: `ReportSaveError` (Task 1); `App.ShowExceptionReport(Exception)`.
- Produces: user-facing behaviour only; nothing downstream.

- [x] **Step 1: Make `App.ShowExceptionReport` public**

In `DialogEditor.Avalonia\App.axaml.cs` line 92:

```csharp
    public void ShowExceptionReport(Exception ex)
```

(body unchanged).

- [x] **Step 2: Wire the delegate in MainWindow**

In `DialogEditor.Avalonia\Views\MainWindow.axaml.cs`, directly after the `vm.ShowImportWarnings = ...` block (line 89):

```csharp
        vm.ReportSaveError = ex =>
            (Application.Current as App)?.ShowExceptionReport(ex);
```

If `Application` is unresolved, add `using Avalonia;` to the file's usings.

- [x] **Step 3: Fix the bare catch in ExceptionReportWindow**

In `DialogEditor.Avalonia\Views\ExceptionReportWindow.axaml.cs`, replace `IssuesLink_Click`:

```csharp
    private void IssuesLink_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ExceptionReportViewModel vm) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(vm.IssuesUrl) { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Warn($"ExceptionReportWindow: could not open issues link — {ex.Message}"); }
    }
```

Add `using DialogEditor.ViewModels.Services;` to the file's usings (for `AppLog`).

- [x] **Step 4: Full suite + solution build**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (all).

Run: `dotnet build DialogEditor.slnx`
Expected: Build succeeded, 0 errors.

Manual smoke check when convenient: make the open `.dialogproject` read-only, Ctrl+S → the exception report window appears (once per exception type) alongside the status-bar error.

- [x] **Step 5: Commit**

```powershell
git add DialogEditor.Avalonia/App.axaml.cs DialogEditor.Avalonia/Views/MainWindow.axaml.cs DialogEditor.Avalonia/Views/ExceptionReportWindow.axaml.cs
git commit -m @'
feat(save): show ExceptionReportWindow on save failures

Wires MainWindowViewModel.ReportSaveError to App.ShowExceptionReport
(now public; per-type dedupe unchanged). Also replaces the bare catch
in ExceptionReportWindow.IssuesLink_Click with a logged catch per the
error-handling rule.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```
