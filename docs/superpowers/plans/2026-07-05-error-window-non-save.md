# Error Window for Non-Save Failures Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every `AppLog.Error`-level failure in `MainWindowViewModel` surfaces the `ExceptionReportWindow`, not just save failures.

**Architecture:** Rename the `ReportSaveError` delegate to `ReportError`; invoke it from every catch block that logs at Error severity; change the `MainWindow` wiring to post window creation to the UI thread (some call sites run in `Task.Run`); enforce the rule with a `NoStrayHexTests`-style source-scan test so new error sites can't regress to status-bar-only.

**Tech Stack:** C# / .NET, Avalonia, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-05-error-window-non-save-design.md`

## Global Constraints

- Strict red/green TDD — failing test before implementation (CLAUDE.md).
- Triage rule (spec): catch blocks logging `AppLog.Error` invoke `ReportError`; `AppLog.Warn` catch blocks stay status/log-only.
- Scope: `MainWindowViewModel` only — git tool windows keep in-window reporting.
- Wiring must post to the UI thread (`Dispatcher.UIThread.Post`), mirroring `App`'s domain/task hooks; the ViewModel layer stays dispatcher-free.
- `CopyVoFolder` keeps its return-the-exception pattern (`return ex;`), caller invokes.
- No new user-visible strings; status-bar behaviour unchanged.
- `DialogEditor.Tests` runs serially — do not change test parallelisation.

---

### Task 1: Rename `ReportSaveError` → `ReportError` + UI-thread post (refactor under green)

**Files:**
- Modify: `DialogEditor.ViewModels\ViewModels\MainWindowViewModel.cs` (property + 4 invoke sites)
- Modify: `DialogEditor.Tests\ViewModels\MainWindowViewModelSaveAsTests.cs` (5 test usages)
- Modify: `DialogEditor.Avalonia\Views\MainWindow.axaml.cs` (wiring)

**Interfaces:**
- Consumes: existing `public Action<Exception>? ReportSaveError { get; set; }`.
- Produces: `public Action<Exception>? ReportError { get; set; }` — same semantics; Task 2's new call sites and tests use this name.

- [x] **Step 1: Confirm green**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (1890).

- [x] **Step 2: Rename every occurrence**

Replace the identifier `ReportSaveError` with `ReportError` in all three files
(`Grep "ReportSaveError"` must return zero hits afterwards). The property's doc
comment becomes:

```csharp
    /// Set by the UI layer to surface a caught operation failure (save, open,
    /// import, …) in the exception report window — status-bar text alone is too
    /// easy to miss for a failed operation.
    public Action<Exception>? ReportError { get; set; }
```

- [x] **Step 3: Post the wiring to the UI thread**

In `DialogEditor.Avalonia\Views\MainWindow.axaml.cs`, the wiring becomes:

```csharp
        // Post: some ReportError call sites run off the UI thread (e.g. the VO
        // alias index rebuild in Task.Run), and window creation must not.
        vm.ReportError = ex =>
            Dispatcher.UIThread.Post(() => (Application.Current as App)?.ShowExceptionReport(ex));
```

Add `using Avalonia.Threading;` to the file's usings if not present.

- [x] **Step 4: Verify green**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (1890) — pure rename, no behaviour change at the VM layer.

- [x] **Step 5: Commit**

```powershell
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelSaveAsTests.cs DialogEditor.Avalonia/Views/MainWindow.axaml.cs
git commit -m @'
refactor(errors): rename ReportSaveError to ReportError, post to UI thread

Prepares the delegate for non-save error families; the wiring now posts
window creation to the UI thread so background call sites are safe.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 2: Invoke `ReportError` from every Error-level catch (TDD)

**Files:**
- Create: `DialogEditor.Tests\ViewModels\ErrorReportingCoverageTests.cs`
- Create: `DialogEditor.Tests\ViewModels\MainWindowViewModelReportErrorTests.cs`
- Modify: `DialogEditor.ViewModels\ViewModels\MainWindowViewModel.cs` (~11 catch blocks)

**Interfaces:**
- Consumes: `ReportError` (Task 1); test helpers `StubStringProvider`, `StubDispatcher`, `StubFolderPicker`, `StubFilePicker`, `StubProvider` (all in `DialogEditor.Tests\Helpers`).
- Produces: nothing downstream — this completes the feature.

- [x] **Step 1: Write the source-scan enforcement test**

Create `DialogEditor.Tests\ViewModels\ErrorReportingCoverageTests.cs`:

```csharp
using System.Text.RegularExpressions;

namespace DialogEditor.Tests.ViewModels;

/// Every catch block in MainWindowViewModel.cs that logs at Error severity must
/// also surface the exception via the ReportError delegate — or hand it to the
/// caller with `return ex;` (the CopyVoFolder pattern). Mirrors the
/// NoStrayHexTests source-scan idiom so a new status-bar-only error site fails
/// the build. Spec: docs/superpowers/specs/2026-07-05-error-window-non-save-design.md
public class ErrorReportingCoverageTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void EveryErrorLoggingCatchAlsoReportsToTheErrorWindow()
    {
        var path = Path.Combine(SolutionRoot(),
            "DialogEditor.ViewModels", "ViewModels", "MainWindowViewModel.cs");
        var source = File.ReadAllText(path);

        var offenders = new List<string>();
        foreach (Match m in Regex.Matches(source, @"catch\s*(\([^)]*\))?\s*\{"))
        {
            var block = ExtractBraceBlock(source, source.IndexOf('{', m.Index));
            if (!block.Contains("AppLog.Error")) continue;
            if (block.Contains("ReportError?.Invoke") || block.Contains("return ex;")) continue;
            offenders.Add($"line {source[..m.Index].Count(c => c == '\n') + 1}");
        }

        Assert.True(offenders.Count == 0,
            "Catch blocks logging AppLog.Error without surfacing via ReportError "
            + "(add ReportError?.Invoke(ex), or `return ex;` with the caller invoking):\n"
            + string.Join('\n', offenders));
    }

    /// Returns the brace-delimited block starting at openBraceIndex (inclusive).
    private static string ExtractBraceBlock(string source, int openBraceIndex)
    {
        var depth = 0;
        for (var i = openBraceIndex; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
                return source.Substring(openBraceIndex, i - openBraceIndex + 1);
        }
        return source[openBraceIndex..];
    }
}
```

- [x] **Step 2: Write the representative behavioural tests**

Create `DialogEditor.Tests\ViewModels\MainWindowViewModelReportErrorTests.cs`:

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// ReportError must fire for representative non-save failures (project open,
/// conversation import); the remaining Error-level sites are covered
/// structurally by ErrorReportingCoverageTests.
public class MainWindowViewModelReportErrorTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _tempDir;

    public MainWindowViewModelReportErrorTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_reporterr_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _tempDir = Path.Combine(Path.GetTempPath(), $"reporterr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_tempDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm(string? openResult = null) =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker(openResult: openResult));

    private static Task InvokeLoadProjectAsync(MainWindowViewModel vm, string path)
    {
        var mi = typeof(MainWindowViewModel).GetMethod("LoadProjectAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Task)mi.Invoke(vm, [path, false])!;
    }

    private static void InjectProvider(MainWindowViewModel vm, IGameDataProvider provider)
    {
        var fi = typeof(MainWindowViewModel).GetField("_provider",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        fi.SetValue(vm, provider);
    }

    private static void InjectProject(MainWindowViewModel vm, DialogProject project)
    {
        var mi = typeof(MainWindowViewModel).GetMethod("SetProject",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        mi.Invoke(vm, [project]);
    }

    [Fact]
    public async Task OpenCorruptProject_InvokesReportError()
    {
        var path = Path.Combine(_tempDir, "corrupt.dialogproject");
        File.WriteAllText(path, "{ this is not valid json");
        var vm = MakeVm();
        Exception? reported = null;
        vm.ReportError = ex => reported = ex;

        await InvokeLoadProjectAsync(vm, path);

        Assert.NotNull(reported);
    }

    [Fact]
    public async Task OpenValidProject_DoesNotInvokeReportError()
    {
        var path = Path.Combine(_tempDir, "ok.dialogproject");
        DialogProjectSerializer.SaveToFile(path, DialogProject.Empty("ok"));
        var vm = MakeVm();
        Exception? reported = null;
        vm.ReportError = ex => reported = ex;

        await InvokeLoadProjectAsync(vm, path);

        Assert.Null(reported);
    }

    [Fact]
    public async Task ImportConversation_MissingFile_InvokesReportError()
    {
        // Picker returns a path that doesn't exist — the import's file read throws.
        var vm = MakeVm(openResult: Path.Combine(_tempDir, "does-not-exist.yarn"));
        var file = new ConversationFile("stub_conv", "", "", "");
        InjectProvider(vm, new StubProvider(file, new ConversationEditSnapshot([])));
        InjectProject(vm, DialogProject.Empty("p"));
        Exception? reported = null;
        vm.ReportError = ex => reported = ex;

        await vm.ImportConversationCommand.ExecuteAsync(null);

        Assert.NotNull(reported);
    }
}
```

- [x] **Step 3: Run to verify red**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ErrorReporting|FullyQualifiedName~ReportErrorTests"`
Expected: `EveryErrorLoggingCatchAlsoReportsToTheErrorWindow` FAILS listing ~11 offender lines; `OpenCorruptProject_InvokesReportError` and `ImportConversation_MissingFile_InvokesReportError` FAIL (`reported` null); `OpenValidProject_DoesNotInvokeReportError` passes.

- [x] **Step 4: Add `ReportError?.Invoke(ex);` to every offending catch**

In `MainWindowViewModel.cs`, for each line the scan test reported, add
`ReportError?.Invoke(ex);` as the last statement of the catch block (after the
`StatusText` assignment where present). The sites (locate each with
`Grep "AppLog.Error" MainWindowViewModel.cs`): project-wide batch VO scan,
project open (`LoadProjectAsync`), conversation import, apply-from-diff save,
undo-apply save, merge projects, VO alias index rebuild (single-line catch —
becomes two statements), game-data initialisation, sample build, backup,
restore, test-apply, per-file VO sync copy. Example for the single-line catch:

```csharp
                    catch (Exception ex)
                    {
                        AppLog.Error($"VO alias index rebuild failed: {ex.Message}");
                        ReportError?.Invoke(ex);
                    }
```

Do NOT touch any `AppLog.Warn` catch, and do NOT change `CopyVoFolder`
(its `return ex;` already satisfies the rule).

- [x] **Step 5: Run the new tests, then the full suite**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ErrorReporting|FullyQualifiedName~ReportErrorTests"`
Expected: PASS (4).

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (1894).

- [x] **Step 6: Commit**

```powershell
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/ErrorReportingCoverageTests.cs DialogEditor.Tests/ViewModels/MainWindowViewModelReportErrorTests.cs
git commit -m @'
feat(errors): surface all Error-level failures in the exception window

Every MainWindowViewModel catch that logs AppLog.Error now also invokes
ReportError (open, import, merge, game-data load, backup/restore,
test-apply, batch VO, sample build, apply/undo-apply, VO sync/index).
ErrorReportingCoverageTests enforces the rule structurally; Warn-level
catches stay status-bar-only by design.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 3: Close the gap entry

**Files:**
- Modify: `Gaps.md` (the `### Non-save errors are status-bar-only` entry)

**Interfaces:** none — documentation only.

- [x] **Step 1: Mark the gap implemented**

Replace the entry body with:

```markdown
### ~~Non-save errors are status-bar-only~~ ✓ Implemented (2026-07-05)
Every `MainWindowViewModel` catch that logs `AppLog.Error` (open, import, merge,
game-data load, backup/restore, test-apply, batch VO, sample build, apply/undo-apply
saves, VO sync/index) now also surfaces the exception in `ExceptionReportWindow` via
the renamed `ReportError` delegate; the wiring posts to the UI thread so background
sites are safe, and the window's per-type dedupe prevents floods.
`ErrorReportingCoverageTests` enforces the rule structurally. `AppLog.Warn` sites stay
status-bar-only by design; git tool windows keep their in-window reporting.
Spec: docs/superpowers/specs/2026-07-05-error-window-non-save-design.md.
```

- [x] **Step 2: Full suite**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS.

- [x] **Step 3: Commit**

```powershell
git add Gaps.md
git commit -m @'
docs(gaps): mark non-save error visibility gap implemented

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```
