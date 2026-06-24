# VO Import & Mod Bundle Design Spec

**Date:** 2026-06-25
**Scope:** `NodeDetailViewModel`, `NodeDetailView`, `ConversationView`, `MainWindow`,
`PatchService`, Patch Manager, `dialog-patcher` CLI. Adds `IVoImporter`,
`VoImportDialog`, `_vo/` storage convention, and the `.dialogpack` bundle format.

---

## Goal

Let users place custom or replacement PoE2 `.wem` voice-over files at the correct
game path from inside the editor. Two source formats are accepted:

- **Pre-encoded `.wem`** — copied directly; no external tool required.
- **Raw `.wav`** — encoded to `.wem` via Wwise CLI; Wwise must be installed.

Imported files are stored in a `_vo/` folder next to the `.dialogproject` and
synced to the game folder at Test Patch time. A single "Export Mod Bundle…" action
packs the project and audio into a distributable `.dialogpack` file.

---

## Storage Convention — `_vo/` folder

Imported VO files live in a `_vo/` folder **co-located with the `.dialogproject`**,
mirroring the game's VO directory structure:

```
MyMod/
  mymod.dialogproject
  _vo/
    eder/
      testline_0001.wem
      testline_0001_fem.wem
    narrator/
      testline_0002.wem
```

- The `_vo/` folder name is fixed by convention — no configuration needed.
- The `.dialogproject` format is **unchanged** — audio is not embedded in it.
- `VoPathResolver` already resolves the expected relative path for every node,
  so the import destination is always unambiguous.
- **Unsaved project guard:** import is disabled until the project has been saved
  at least once (the `_vo/` folder needs an anchor on disk). The import button
  shows a tooltip explaining this when the project is unsaved.

### Test Patch / Restore integration

**F5 (Test Patch):** after patching conversation files, the editor scans `_vo/`
and copies each file to the game's VO directory
(`<gameRoot>/PillarsOfEternityII_Data/StreamingAssets/Audio/Windows/Voices/English(US)/`).
Any file being overwritten is backed up first using the same per-file backup
mechanism as conversation patches.

**F6 (Restore):** removes copied VO files and restores backups.

---

## The `.dialogpack` Bundle Format

A `.dialogpack` is a **standard ZIP archive** with a custom extension.
Users can rename it to `.zip` to inspect or extract the contents with any
standard archive tool.

### Structure

```
mymod.dialogpack
├── project.dialogproject
├── vo/
│   ├── eder/
│   │   ├── testline_0001.wem
│   │   └── testline_0001_fem.wem
│   └── narrator/
│       └── testline_0002.wem
└── FORMAT.md
```

`project.dialogproject` is always at the root with this fixed name.
`vo/` mirrors the `_vo/` folder structure (without the leading underscore).
`FORMAT.md` is bundled in every pack and documents the layout (see below).

### FORMAT.md contents

```markdown
# .dialogpack format

A `.dialogpack` file is a standard ZIP archive. Rename it to `.zip` to
inspect or extract its contents with any archive tool.

Contents:
- `project.dialogproject` — the dialog diff (JSON); apply with the
  Pillars Dialog Editor Patch Manager or the `dialog-patcher` CLI.
- `vo/` — voice-over audio files in Wwise `.wem` format, laid out to
  mirror the game's VO directory structure. The Patch Manager and CLI
  copy these to the correct game folder location when applying the pack.
- `FORMAT.md` — this file.
```

### Export — "File › Export Mod Bundle…"

Packages the open `.dialogproject` and the entire `_vo/` folder into a
`.dialogpack`. Disabled (menu item greyed out) when no `_vo/` folder exists
alongside the current project — in that case the plain `.dialogproject` is
the correct thing to share.

### Patch Manager and CLI support

Both tools already accept `.dialogproject` files. They additionally detect the
`.dialogpack` extension, unzip to a temp folder, process `project.dialogproject`
as normal, and copy the `vo/` files to the game's VO directory.

All ZIP work uses `System.IO.Compression` — no new NuGet dependency.

---

## Import UI

### Entry points

1. **Node detail panel** — a `🎤` import button in the VO status row, positioned
   after the existing play buttons. Visible whenever the selected node has a
   resolvable VO path (i.e. `HasVoStatus` is true), regardless of whether a file
   already exists. Disabled with an explanatory tooltip when the project is unsaved.

2. **Canvas context menu** — "Import voice-over…" item on the node right-click
   menu, enabled on the same condition.

Both entry points open the same `VoImportDialog`.

### `VoImportDialog`

A modal dialog with two file slots:

```
┌─ Import Voice-Over ──────────────────────────────────────────┐
│                                                              │
│  Primary           [Browse…]  testline_0001.wem  [✕]        │
│  Female (optional) [Browse…]  —                              │
│                                                              │
│  ⚠ Wwise not found — WAV files cannot be encoded.           │
│     [Download Wwise]                                         │
│                                                              │
│                                           [Cancel]  [OK]    │
└──────────────────────────────────────────────────────────────┘
```

- Both slots accept `.wav` and `.wem` via the file picker (both extensions shown).
- **`.wem` picked:** copied directly to `_vo/` on OK — no Wwise needed.
- **`.wav` picked, Wwise found:** encoded to `.wem` via Wwise CLI; result placed
  in `_vo/`.
- **`.wav` picked, Wwise absent:** the Wwise warning and "Download Wwise" link
  appear; OK is disabled for that slot only. If the other slot has a valid `.wem`,
  OK remains enabled for it.
- Primary is required; female is optional. OK is enabled as long as primary has a
  valid, processable file.
- After OK: the VO status row refreshes immediately (✓ if primary now exists in
  `_vo/`; play button activates).

### Wwise detection

Checked once at `VoImporter` construction, result cached in `IsWwiseAvailable`:

1. `WWISEROOT` environment variable → look for `WwiseCLI.exe` relative to it.
2. Registry key `HKEY_LOCAL_MACHINE\SOFTWARE\Audiokinetic\Wwise\` → enumerate
   installed versions, pick the newest, build the path to `WwiseCLI.exe`.
3. Common install path fallback:
   `C:\Program Files (x86)\Audiokinetic\Wwise <version>\Authoring\x64\Release\bin\WwiseCLI.exe`

If none found, `IsWwiseAvailable = false`. The "Download Wwise" link opens
`https://www.audiokinetic.com/en/products/wwise/` in the default browser.

---

## Architecture

### Service interface (`DialogEditor.ViewModels`)

```csharp
// IVoImporter.cs
public interface IVoImporter
{
    bool IsWwiseAvailable { get; }
    Task<VoImportResult> ImportAsync(VoImportRequest request, CancellationToken ct);
}

public sealed class NullVoImporter : IVoImporter
{
    public static readonly NullVoImporter Instance = new();
    private NullVoImporter() { }
    public bool IsWwiseAvailable => false;
    public Task<VoImportResult> ImportAsync(VoImportRequest request, CancellationToken ct)
        => Task.FromResult(new VoImportResult(false, "No importer configured."));
}

// VoImportRequest.cs
public record VoImportRequest(
    string  DestinationPath,       // full path to _vo/<speaker>/<file>.wem
    string  SourcePath,            // .wav or .wem picked by user
    string? FemDestinationPath,    // null if no female slot provided
    string? FemSourcePath);

// VoImportResult.cs
public record VoImportResult(bool Success, string? ErrorMessage);
```

Follows the exact pattern of `IVoAudioPlayer` / `NullVoAudioPlayer`.

### `NodeDetailViewModel` additions

- `Importer` settable property (same pattern as `Player`):
  ```csharp
  public IVoImporter Importer { get; set; } = NullVoImporter.Instance;
  ```
- `ProjectPath` settable property (same pattern as `GameRoot` / `ActiveGameId`),
  set by `MainWindowViewModel` when a project is opened or saved.
- `CanImportVo` computed property: `HasVoStatus && ProjectPath is not null`
- `[RelayCommand] async ImportVo()`: resolves `_vo/` destination paths from
  `_voCheck` (using `Path.GetDirectoryName(ProjectPath)` as the anchor), opens
  `VoImportDialog`, calls `Importer.ImportAsync(...)`, then re-runs the VO check
  to refresh the status row.

### `VoImporter` (`DialogEditor.Avalonia/Audio/VoImporter.cs`)

Concrete implementation:

- Detects Wwise at construction (caches `IsWwiseAvailable` and the CLI path).
- `ImportAsync`: for each slot, if source is `.wem` → `File.Copy`; if `.wav` →
  shell out to `WwiseCLI.exe` with the appropriate encode flags (exact flags to
  be determined during implementation by testing against a real Wwise install),
  then move the output to the destination. Creates `_vo/` subdirectories as needed.
- Every caught exception (except `OperationCanceledException`) logged via
  `AppLog.Error` and returned as a failed `VoImportResult`.

### `MainWindow.axaml.cs` wiring

```csharp
var importer = new VoImporter();
vm.Detail.Importer = importer;
// (alongside the existing: vm.Detail.Player = audioPlayer;)
```

No disposal needed — `VoImporter` holds no unmanaged resources.

---

## Files to Create / Modify

| File | Change |
|------|--------|
| `DialogEditor.ViewModels/Services/IVoImporter.cs` | **Create** — interface + `NullVoImporter` + `VoImportRequest` + `VoImportResult` |
| `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs` | Add `Importer` property, `CanImportVo`, `ImportVoCommand` |
| `DialogEditor.Avalonia/Audio/VoImporter.cs` | **Create** — Wwise detection + WAV→WEM + file copy |
| `DialogEditor.Avalonia/Views/VoImportDialog.axaml` | **Create** — two-slot dialog XAML |
| `DialogEditor.Avalonia/Views/VoImportDialog.axaml.cs` | **Create** — dialog code-behind |
| `DialogEditor.Avalonia/Views/NodeDetailView.axaml` | Add `🎤` import button to VO status row |
| `DialogEditor.Avalonia/Views/ConversationView.axaml` | Add "Import voice-over…" to node context menu |
| `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` | Wire `VoImporter` into `vm.Detail.Importer` |
| `DialogEditor.Avalonia/Services/VoPackExporter.cs` | **Create** — "Export Mod Bundle…" logic (ZIP) |
| `DialogEditor.Avalonia/Views/MainWindow.axaml` | Add "File › Export Mod Bundle…" menu item |
| `DialogEditor.Avalonia/Resources/Strings.axaml` | New strings (see below) |
| `DialogEditor.Patch/PatchApplier.cs` + `MainWindowViewModel.cs` | Extend F5/F6 to sync `_vo/` with game folder |
| `DialogEditor.PatchManager/…` | Detect `.dialogpack`, unzip, process |
| `DialogEditor.Cli/…` | Detect `.dialogpack`, unzip, process |
| `DialogEditor.Tests/ViewModels/NodeDetailViewModelImportTests.cs` | **Create** |
| `FORMAT.md` | **Create** at repo root (bundled into every `.dialogpack`) |

---

## New Strings (`Strings.axaml`)

```xml
<!-- ── VO import ──────────────────────────────────────────────────── -->
<sys:String x:Key="VoImport_DialogTitle">Import Voice-Over</sys:String>
<sys:String x:Key="VoImport_PrimaryLabel">Primary</sys:String>
<sys:String x:Key="VoImport_FemaleLabel">Female (optional)</sys:String>
<sys:String x:Key="VoImport_Browse">Browse…</sys:String>
<sys:String x:Key="VoImport_NoWwise">Wwise not found — WAV files cannot be encoded.</sys:String>
<sys:String x:Key="VoImport_DownloadWwise">Download Wwise</sys:String>
<sys:String x:Key="VoImport_UnsavedProject">Save the project first — the _vo/ folder needs a location on disk.</sys:String>
<sys:String x:Key="ToolTip_VoImport">Import a voice-over file (.wem or .wav) for this node.</sys:String>
<sys:String x:Key="ToolTip_VoImport_Unsaved">Save the project before importing voice-over files.</sys:String>
<sys:String x:Key="AutomationName_VoImport">Import voice-over</sys:String>
<sys:String x:Key="Menu_ImportVo">Import voice-over…</sys:String>
<sys:String x:Key="ToolTip_Menu_ImportVo">Import a .wem or .wav voice-over file for the selected node.</sys:String>
<sys:String x:Key="Menu_ExportModBundle">Export Mod Bundle…</sys:String>
<sys:String x:Key="ToolTip_Menu_ExportModBundle">Package this project and its voice-over files into a single distributable .dialogpack file.</sys:String>
```

---

## Testing

### Unit tests

**`NodeDetailViewModelImportTests`** (mirrors `NodeDetailViewModelPlaybackTests`):

- `ImportVoCommand_DisabledWhenProjectPathIsNull`
- `ImportVoCommand_DisabledWhenNoVoStatus`
- `ImportVo_OnSuccess_RefreshesVoStatus` — stub returns success; assert `VoStatusIsFound`
- `ImportVo_OnFailure_DoesNotChangeStatus`

### Manual verification checklist

- [ ] Import a `.wem` directly → file appears in `_vo/`, status flips to ✓, play button activates
- [ ] Import a `.wav` with Wwise installed → same result
- [ ] Import with Wwise absent + `.wav` picked → OK disabled for that slot, "Download Wwise" link visible; `.wem` slot still works
- [ ] Import button disabled (with tooltip) on an unsaved project
- [ ] F5 copies `_vo/` files to game VO directory; F6 removes them and restores originals
- [ ] Replacing an existing `.wem` via import: F5 backs up original, F6 restores it
- [ ] "File › Export Mod Bundle…" produces a `.dialogpack`; renaming to `.zip` shows expected structure with `FORMAT.md`
- [ ] "File › Export Mod Bundle…" is greyed out when no `_vo/` folder exists
- [ ] Patch Manager accepts `.dialogpack` and applies both dialog diff and VO files
- [ ] `dialog-patcher mymod.dialogpack <game-dir>` applies both dialog diff and VO files
- [ ] Female slot: import a female `.wem` → status updates to "Found (with female variant)", ▶ F button appears

---

## Out of Scope

- PoE1 VO (Unity binary asset bundles — deferred indefinitely)
- Mod VO (mods that add/replace lines across multiple speakers — deferred)
- Batch import (multiple nodes at once)
- Re-encoding quality settings (Wwise defaults are used)
- Audio preview of the imported file before committing
