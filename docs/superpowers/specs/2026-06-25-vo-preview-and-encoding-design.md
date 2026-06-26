# VO Preview and WAV→WEM Encoding Design

**Date:** 2026-06-25
**Status:** Approved
**Scope:** Two deferred VO import features — in-dialog audio preview and the WAV→WEM encoding pipeline with quality presets.

---

## Background

The initial VO import feature (`2026-06-25-vo-import-design.md`) deferred four items. This spec covers two of them:

1. **Audio preview** — play a picked file inside `VoImportDialog` before clicking OK.
2. **WAV→WEM encoding + quality settings** — the encoding path was a placeholder; this spec completes it with a bundled Wwise project template and a three-level quality preset.

The other two deferred items (Batch import, Mod VO) remain out of scope.

---

## Feature 1: Audio Preview

### Goal

Let the user audition a picked `.wem` or `.wav` file inside `VoImportDialog` before committing the import. An explicit ▶/■ toggle button appears next to each slot once a file is selected.

### Architecture

**Approach:** Inject the existing shared `IVoAudioPlayer` instance into `VoImportDialog`. The player is already owned by `MainWindow`; sharing it serialises all audio output — clicking ▶ in the dialog naturally stops any node-level playback that was in progress.

### Changes

#### `VoAudioPlayer` — `.wav` short-circuit

`Play(string path)` currently always forks to vgmstream for decoding. Add a branch before the vgmstream spawn:

```
if path ends with ".wav" (case-insensitive):
    open path directly with NAudio AudioFileReader
    play via the existing WaveOutEvent pipeline
    skip vgmstream entirely
```

The `_generation` cancellation token, `PlaybackStopped` event, and temp-file cleanup logic are unchanged. `IsAvailable` (which gates vgmstream availability) must NOT block `.wav` playback — `.wav` files play regardless of whether vgmstream is installed.

#### `VoImportDialog` — constructor + state

Constructor signature:

```csharp
VoImportDialog(IVoImporter importer, VoImportPaths paths, IVoAudioPlayer player)
```

New fields:

- `_player` — stored reference
- `_playingSlot` — value of a private `enum PlayingSlot { None, Primary, Fem }` declared inside the class, tracking which slot's button shows ■

On `Closed`: call `_player.Stop()`.

Subscribe to `_player.PlaybackStopped` in the constructor; in the handler, reset `_playingSlot = None` and update both button glyphs to ▶.

#### `VoImportDialog.axaml` — new controls

Each slot row gains a ▶/■ toggle button to the left of "Browse…":

- `IsVisible="False"` until a file is selected for that slot (set in `BrowsePrimary_Click` / `BrowseFem_Click`)
- `IsVisible="False"` again in the corresponding `Clear*_Click` handler
- Clicking ▶: call `_player.Play(sourcePath)`, set `_playingSlot`, flip glyph to ■, flip the other slot's button to ▶ if it was active
- Clicking ■: call `_player.Stop()`, set `_playingSlot = None`, flip glyph to ▶

Buttons carry `ToolTip.Tip`, `AutomationProperties.Name`, and `AutomationProperties.HelpText`.

#### `MainWindow.axaml.cs` — wiring update

```csharp
vm.Detail.ShowImportDialog = async paths =>
{
    var dialog = new VoImportDialog(voImporter, paths, audioPlayer);
    await dialog.ShowDialog(this);
    return dialog.Result;
};
```

`audioPlayer` is the same `VoAudioPlayer` instance already wired to `vm.Detail.Player`.

#### New strings (`Strings.axaml`)

| Key | Value |
|-----|-------|
| `ToolTip_VoPreviewPlay_Primary` | Play the selected primary voice-over file. |
| `ToolTip_VoPreviewPlay_Fem` | Play the selected female voice-over file. |
| `ToolTip_VoPreviewStop` | Stop playback. |
| `AutomationName_VoPreviewPlay_Primary` | Play primary voice-over |
| `AutomationName_VoPreviewPlay_Fem` | Play female voice-over |
| `AutomationName_VoPreviewStop` | Stop voice-over preview |

#### Tests

`VoAudioPlayerTests` — add a test verifying that `Play("file.wav")` does not throw and does not attempt to spawn vgmstream when `IsAvailable` is false. Use a real `.wav` file in `TestAssets/` or a NAudio-generated in-memory WAV written to a temp file.

No automated UI tests for `VoImportDialog` itself (consistent with existing precedent).

---

## Feature 2: WAV→WEM Encoding Pipeline + Quality Presets

### Goal

Complete the blocked `.wav` encoding path in `VoImporter` and expose a Low / Medium / High quality preset in `VoImportDialog`, applied when the source file is `.wav`.

### Architecture

**Approach:** Bundle a minimal `template.wproj` in `DialogEditor.Avalonia/Assets/Wwise/` that defines three named Vorbis conversion presets. At encode time, generate a per-encode `.wsources` XML file naming the chosen preset, then invoke `WwiseCLI.exe`. Quality flows through `VoImportDialogResult` → `VoImportRequest` → `VoImporter`.

### `WemQuality` enum

Added to `IVoImporter.cs` alongside the existing record types:

```csharp
public enum WemQuality { Low, Medium, High }
```

Default throughout: `Medium`.

### Record changes

```csharp
// VoImportDialogResult gains:
public record VoImportDialogResult(
    string  PrimarySourcePath,
    string? FemSourcePath,
    WemQuality Quality = WemQuality.Medium);

// VoImportRequest gains:
public record VoImportRequest(
    string  PrimaryDestinationPath,
    string  PrimarySourcePath,
    string? FemDestinationPath,
    string? FemSourcePath,
    WemQuality Quality = WemQuality.Medium);
```

`NodeDetailViewModel.ImportVo` passes `selection.Quality` when constructing `VoImportRequest`.

### Bundled asset: `template.wproj`

**Location:** `DialogEditor.Avalonia/Assets/Wwise/template.wproj`
**Build action:** `<AvaloniaResource>`

A minimal Wwise project XML defining exactly three Vorbis conversion presets:

| Preset name | Vorbis quality | Sample rate |
|-------------|---------------|-------------|
| `VorbisLow` | 0.3 | Match source |
| `VorbisMedium` | 0.6 | Match source |
| `VorbisHigh` | 0.9 | Match source |

No sound banks, events, buses, or platform-specific settings beyond what `WwiseCLI.exe` requires to process a `.wsources` file for the Windows platform.

> **Verification checkpoint:** The exact `.wproj` XML schema is Wwise-version-dependent. The template must be authored and round-tripped against a real Wwise install before the encoding path can ship. All uncertainty is isolated to this one file — the surrounding code compiles and tests without it.

### `VoImporter` — encoding implementation

Replace the `throw InvalidOperationException` in `ProcessSlotAsync` for `.wav` with a call to:

```
private async Task EncodeWavToWemAsync(
    string sourcePath, string destPath, WemQuality quality, CancellationToken ct)
```

#### `GetOrExtractTemplateWproj()` — private, cached

On first call:
1. Load `avares://DialogEditor.Avalonia/Assets/Wwise/template.wproj` via `AssetLoader.Open(uri)`.
2. Write to `%TEMP%\PillarsDialogEditor\wwise\template.wproj` (overwriting any leftover from a previous session).
3. Store the path in a `static string? _cachedWprojPath` field.

Returns the cached path on all subsequent calls within the same app session.

#### `GenerateWsourcesXml(sourcePath, destPath, quality)` — private, pure

Returns an XML string:

```xml
<ExternalSourcesList SchemaVersion="1" Root="{sourceDir}">
  <Source Path="{sourceFileName}"
          Destination="{destNameWithoutExtension}"
          Conversion="{presetName}"/>
</ExternalSourcesList>
```

Where `presetName` maps `WemQuality` → `"VorbisLow"` / `"VorbisMedium"` / `"VorbisHigh"`.

This method is pure (no I/O) and is the primary test target.

#### `EncodeWavToWemAsync` — full flow

1. Call `GetOrExtractTemplateWproj()` → `wprojPath`
2. Create a temp directory: `%TEMP%\PillarsDialogEditor\wwise\encode_{guid}`
3. Write `GenerateWsourcesXml(...)` to `{tempDir}\sources.wsources`
4. Run:
   ```
   WwiseCLI.exe "{wprojPath}" -Platform Windows -ConvertExternalSources "{tempDir}\sources.wsources"
   ```
5. Locate the output `.wem` — Wwise writes to `{wprojDir}\GeneratedSoundBanks\Windows\{destNameWithoutExtension}.wem`
   (exact output path to be confirmed during implementation against a real install)
6. `File.Move(outputWem, destPath, overwrite: true)`
7. Delete `tempDir` (best-effort, in a `finally` block)

On `OperationCanceledException`: rethrow (per project convention — OCE is swallowed silently by the caller).
On any other exception: let it propagate to `ProcessSlotAsync` → `ImportAsync`, where it is caught, logged via `AppLog.Error`, and returned as `VoImportResult(false, ex.Message)`.

### `VoImportDialog` — quality UI

A quality row is added below the two file-picker rows and above the Wwise warning panel:

```
[ Quality (WAV only) ]  ○ Low  ● Medium  ○ High
```

- Implemented as three `RadioButton` controls in a horizontal `StackPanel`
- `IsEnabled` bound to a code-behind bool `_anyWavSelected`, updated in `UpdateWwiseWarning()` (which already tracks this state)
- `_quality` field (`WemQuality`, default `Medium`) updated by the radio button handlers
- Read when OK is clicked and packed into `VoImportDialogResult`

The row is always visible (no layout shift when switching between `.wem` and `.wav` files); it is simply disabled and visually dimmed when no `.wav` is selected.

#### New strings (`Strings.axaml`)

| Key | Value |
|-----|-------|
| `VoImport_QualityLabel` | Quality (WAV only) |
| `VoImport_Quality_Low` | Low |
| `VoImport_Quality_Medium` | Medium |
| `VoImport_Quality_High` | High |
| `ToolTip_VoQuality` | Encoding quality for WAV files. Low uses less disk space; High preserves more detail. Has no effect on pre-encoded .wem files. |

### Tests

#### `VoImporterTests` (new test class)

**`GenerateWsourcesXml_ContainsCorrectPresetName`** — for each `WemQuality` value, assert the returned XML contains the expected preset name string and the correct source file name. Pure string test, no I/O.

**`GenerateWsourcesXml_UsesForwardSlashesInPaths`** — assert the XML does not contain backslashes (Wwise XML paths use forward slashes).

No test for `EncodeWavToWemAsync` end-to-end (requires a real Wwise install — covered by manual checklist).

---

## Manual Verification Checklist

- [ ] Pick a `.wav` in Primary slot → ▶ button appears; clicking it plays the file; clicking again (■) stops it
- [ ] Pick a `.wem` in Primary slot → ▶ button appears; clicking it plays via vgmstream
- [ ] Pick `.wav` in Primary, click ▶; then click ▶ on Fem slot → Primary stops, Fem starts
- [ ] Close dialog while audio is playing → playback stops
- [ ] With no `.wav` selected, quality row is disabled (dimmed)
- [ ] Pick a `.wav` → quality row enables; select High; click OK → import runs; resulting `.wem` is playable in-node
- [ ] Pick a `.wem` → quality setting ignored (no encoding); import copies file directly
- [ ] With Wwise absent, pick `.wav` → OK disabled, Wwise warning visible, quality row disabled
- [ ] `dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false` — all existing tests pass
