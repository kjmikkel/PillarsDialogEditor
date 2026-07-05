# Export Mod Bundle Gate Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** **File ▸ Export Mod Bundle…** works for any saved project — `vo/` goes into the `.dialogpack` exactly when `_vo/` exists, so text-only mods can finally export.

**Architecture:** Export-side only (consumers already treat `vo/` as optional). `VoPackExporter.ExportAsync` skips the `vo/` loop when the folder is absent; the menu gate property `HasLocalVoFolder` is renamed to `CanExportModBundle` with `ProjectPath is not null` semantics; the embedded `FORMAT.md` and the menu tooltip are reworded. The dead `VoPackExporter.CanExport` is deleted.

**Tech Stack:** C# / .NET 8, Avalonia, xUnit, `System.IO.Compression`.

**Spec:** `docs/superpowers/specs/2026-07-05-export-without-vo-design.md`

## Global Constraints

- Strict red/green TDD — failing test before implementation (CLAUDE.md).
- No new user-visible strings; `ToolTip_Menu_ExportModBundle` is reworded in place (localisation rule: strings live in `Strings.axaml`).
- No consumer-side changes (Patch Manager, CLI, `DialogPackHelper`).
- No with/without-VO choice UI — rejected in the spec's scope decision.
- `DialogEditor.Tests` runs serially — do not change test parallelisation.

---

### Task 1: `VoPackExporter` — optional `vo/` (TDD, first tests for this class)

**Files:**
- Create: `DialogEditor.Tests\Services\VoPackExporterTests.cs`
- Modify: `DialogEditor.Avalonia\Services\VoPackExporter.cs`

**Interfaces:**
- Consumes: `DialogProjectSerializer.SaveToFile` (DialogEditor.Patch), `DialogPackHelper.Extract` (DialogEditor.ViewModels.Services) — both already referenced by the Tests project.
- Produces: `VoPackExporter.ExportAsync(projectPath, outputPath)` that succeeds without a `_vo/` folder. Task 2's gate rename relies on this behaviour existing.

- [ ] **Step 1: Write the tests (one red, one already-green pin)**

Create `DialogEditor.Tests\Services\VoPackExporterTests.cs`:

```csharp
using System.IO.Compression;
using DialogEditor.Avalonia.Services;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

/// A .dialogpack mirrors the project's reality: vo/ is present exactly when the
/// project has a _vo/ folder. A text-only project must export a valid VO-less
/// pack (the gate that used to block this lives in MainWindowViewModel).
/// Spec: docs/superpowers/specs/2026-07-05-export-without-vo-design.md
public class VoPackExporterTests : IDisposable
{
    private readonly string _tempDir;

    public VoPackExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vopack_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private string WriteProject()
    {
        var path = Path.Combine(_tempDir, "mod.dialogproject");
        DialogProjectSerializer.SaveToFile(path, DialogProject.Empty("mod"));
        return path;
    }

    [Fact]
    public async Task Export_WithVoFolder_IncludesVoEntries()
    {
        var projectPath = WriteProject();
        var voDir = Path.Combine(_tempDir, "_vo", "eder");
        Directory.CreateDirectory(voDir);
        File.WriteAllBytes(Path.Combine(voDir, "line_0001.wem"), [1, 2, 3]);
        var output = Path.Combine(_tempDir, "out.dialogpack");

        await VoPackExporter.ExportAsync(projectPath, output);

        var result = DialogPackHelper.Extract(output);
        try
        {
            Assert.NotNull(result.VoFolderPath);
            Assert.True(File.Exists(Path.Combine(result.VoFolderPath!, "eder", "line_0001.wem")));
        }
        finally { Directory.Delete(result.TempDir, recursive: true); }
    }

    [Fact]
    public async Task Export_WithoutVoFolder_ProducesValidVoLessPack()
    {
        var projectPath = WriteProject();
        var output = Path.Combine(_tempDir, "out.dialogpack");

        await VoPackExporter.ExportAsync(projectPath, output);

        using (var zip = ZipFile.OpenRead(output))
        {
            Assert.Contains(zip.Entries, e => e.FullName == "project.dialogproject");
            Assert.Contains(zip.Entries, e => e.FullName == "FORMAT.md");
            Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("vo/"));
        }

        var result = DialogPackHelper.Extract(output);
        try { Assert.Null(result.VoFolderPath); }
        finally { Directory.Delete(result.TempDir, recursive: true); }
    }
}
```

- [ ] **Step 2: Run to verify red**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~VoPackExporterTests"`
Expected: `Export_WithVoFolder_IncludesVoEntries` PASSES (pins existing behaviour);
`Export_WithoutVoFolder_ProducesValidVoLessPack` FAILS — `ExportAsync` calls
`Directory.EnumerateFiles` on the missing `_vo` folder and throws
`DirectoryNotFoundException`.

- [ ] **Step 3: Make `vo/` optional in `ExportAsync`, update `FORMAT.md`, delete dead `CanExport`**

In `DialogEditor.Avalonia\Services\VoPackExporter.cs`:

**(a)** Delete the `CanExport` method entirely (lines ~51-59). It has no callers —
the menu gate binds `MainWindowViewModel.HasLocalVoFolder` directly (renamed in Task 2).

**(b)** In `ExportAsync`, wrap the VO loop in an existence check:

```csharp
                // _vo/ → vo/ inside the archive — present exactly when the project
                // has voice-over; a text-only project exports a valid VO-less pack.
                if (Directory.Exists(voFolder))
                {
                    foreach (var file in Directory.EnumerateFiles(voFolder, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(voFolder, file).Replace('\\', '/');
                        zip.CreateEntryFromFile(file, $"vo/{relative}", CompressionLevel.Optimal);
                    }
                }
```

**(c)** In `FormatMdContent`, change the `vo/` bullet from:

```
        - `vo/` — voice-over audio files in Wwise `.wem` format, laid out to
          mirror the game's VO directory structure. The Patch Manager and CLI
          copy these to the correct game folder location when applying the pack.
```

to:

```
        - `vo/` — voice-over audio files in Wwise `.wem` format, laid out to
          mirror the game's VO directory structure. Present only when the mod
          contains voice-over; the Patch Manager and CLI copy these to the
          correct game folder location when applying the pack.
```

Also update the class doc comment's first line to:

```csharp
/// Packages a .dialogproject — and its _vo/ folder, when one exists — into a
/// .dialogpack file (ZIP with custom extension).
```

- [ ] **Step 4: Run the new tests, then the full suite**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~VoPackExporterTests"`
Expected: PASS (2).

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (1903).

- [ ] **Step 5: Commit**

```powershell
git add DialogEditor.Avalonia/Services/VoPackExporter.cs DialogEditor.Tests/Services/VoPackExporterTests.cs
git commit -m @'
feat(export): dialogpack works without a _vo folder

vo/ is included exactly when the project has voice-over; FORMAT.md
documents it as optional (the consumers always treated it that way).
Dead VoPackExporter.CanExport removed. First tests for this class.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 2: Gate rename `HasLocalVoFolder` → `CanExportModBundle` (TDD)

**Files:**
- Modify: `DialogEditor.Tests\ViewModels\MainWindowViewModelCloseProjectTests.cs` (add one test)
- Modify: `DialogEditor.ViewModels\ViewModels\MainWindowViewModel.cs` (property ~line 237 + 4 raise sites)
- Modify: `DialogEditor.Avalonia\Views\MainWindow.axaml` (`IsEnabled` binding ~line 97)
- Modify: `DialogEditor.Avalonia\Resources\Strings.axaml` (`ToolTip_Menu_ExportModBundle` ~line 1307)

**Interfaces:**
- Consumes: `ExportAsync` without `_vo/` (Task 1); existing test helpers in `MainWindowViewModelCloseProjectTests` (`MakeVm`, `WriteProject`, `InvokeLoadProjectAsync`).
- Produces: `public bool CanExportModBundle` on `MainWindowViewModel` — nothing downstream; this completes the feature.

- [ ] **Step 1: Write the failing test**

Add to `DialogEditor.Tests\ViewModels\MainWindowViewModelCloseProjectTests.cs` (it has
every helper this test needs, and the close-clears-the-gate assertion belongs with the
close tests):

```csharp
    [Fact]
    public async Task CanExportModBundle_TracksSavedProjectState()
    {
        var vm = MakeVm();
        Assert.False(vm.CanExportModBundle);

        var path = WriteProject("p.dialogproject");
        await InvokeLoadProjectAsync(vm, path);
        Assert.True(vm.CanExportModBundle);

        vm.CloseProjectCommand.Execute(null);
        Assert.False(vm.CanExportModBundle);
    }
```

- [ ] **Step 2: Run to verify red**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelCloseProjectTests"`
Expected: FAIL to compile — `'MainWindowViewModel' does not contain a definition for 'CanExportModBundle'`.

- [ ] **Step 3: Rename the gate and change its semantics**

**(a)** In `DialogEditor.ViewModels\ViewModels\MainWindowViewModel.cs`, replace the
property (~line 235):

```csharp
    /// True when a _vo/ folder exists next to the open project file.
    /// Guards the "Export Mod Bundle…" menu item.
    public bool HasLocalVoFolder =>
        ProjectPath is not null &&
        Directory.Exists(Path.Combine(Path.GetDirectoryName(ProjectPath)!, "_vo"));
```

with:

```csharp
    /// True when a saved project is open — gates the "Export Mod Bundle…" menu
    /// item. The pack includes vo/ exactly when _vo/ exists (see VoPackExporter),
    /// so export is meaningful for any saved project, voiced or text-only.
    public bool CanExportModBundle => ProjectPath is not null;
```

**(b)** Rename the remaining occurrences of the identifier `HasLocalVoFolder` —
four `OnPropertyChanged(nameof(HasLocalVoFolder));` raise sites in
`MainWindowViewModel.cs` and the binding in `MainWindow.axaml`:

```xml
                                  IsEnabled="{Binding CanExportModBundle}"
```

Afterwards `Grep "HasLocalVoFolder"` across the repo must return zero hits.

**(c)** In `DialogEditor.Avalonia\Resources\Strings.axaml` (~line 1307), reword the
tooltip:

```xml
    <sys:String x:Key="ToolTip_Menu_ExportModBundle">Package this project into a single distributable .dialogpack file. Voice-over files (the _vo folder) are bundled when the project has them.</sys:String>
```

- [ ] **Step 4: Run the new test, then the full suite**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelCloseProjectTests"`
Expected: PASS (6).

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (1904).

- [ ] **Step 5: Commit**

```powershell
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/ViewModels/MainWindowViewModelCloseProjectTests.cs
git commit -m @'
feat(export): enable Export Mod Bundle for any saved project

HasLocalVoFolder becomes CanExportModBundle (ProjectPath is not null) —
the _vo existence check moved into the exporter, so text-only mods can
export. Tooltip reworded: VO is bundled when present.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 3: Close the gap entry

**Files:**
- Modify: `Gaps.md` (the `### Export Mod Bundle without VO` entry)

**Interfaces:** none — documentation only.

- [ ] **Step 1: Mark the gap resolved (recording the descope)**

Replace the entry body with:

```markdown
### ~~Export Mod Bundle without VO~~ ✓ Resolved by descoping (2026-07-05)
Use-case analysis rejected the proposed with-VO/without-VO export choice: the only
compelling case was that a text-only project (no `_vo/`) could not export a
`.dialogpack` at all — a gating defect, not a missing option. Fixed by making
**File ▸ Export Mod Bundle…** available for any saved project
(`CanExportModBundle`, formerly `HasLocalVoFolder`); the pack contains `vo/`
exactly when `_vo/` exists (consumers always treated `vo/` as optional). The
"exclude my existing VO" cases (small updates to voiced mods, separately
distributed VO) stay unserved until real demand appears.
Spec: docs/superpowers/specs/2026-07-05-export-without-vo-design.md.
```

- [ ] **Step 2: Full suite**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (1904).

- [ ] **Step 3: Commit**

```powershell
git add Gaps.md
git commit -m @'
docs(gaps): mark export-without-vo gap resolved by descoping

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```
