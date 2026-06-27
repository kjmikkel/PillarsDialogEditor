# Wwise Template Asset Design

**Date:** 2026-06-27
**Status:** Approved
**Scope:** Addendum to `2026-06-25-vo-preview-and-encoding-design.md`. Specifies the
bundled Wwise project template used by `VoImporter` for WAV→WEM encoding.

---

## Background

The encoding design deferred the exact content of `template.wproj` as a
"verification checkpoint." This spec closes that gap by specifying the asset
packaging, the semantic requirements for both XML files, and the changes to
`GetOrExtractTemplateWproj()`.

Everything else — `WemQuality` enum, `VoImportDialog` quality UI,
`GenerateWsourcesXml`, `EncodeWavToWemAsync` flow — remains as specified in
the encoding design. This spec does not re-specify those.

---

## Asset Packaging

The existing spec names a single `template.wproj` as an `<AvaloniaResource>`.
A Wwise project is a **folder**, not a single file: `WwiseCLI.exe` expects the
`.wproj` descriptor alongside a `Conversion Settings/` subfolder. The resource
is therefore a ZIP:

**Location:** `DialogEditor.Avalonia/Assets/Wwise/template.wwise.zip`
**Build action:** `<AvaloniaResource>`

ZIP contents:
```
template/
  template.wproj
  Conversion Settings/
    Factory.wwu
```

The subfolder name `Conversion Settings/` and filename `Factory.wwu` are not
arbitrary — they are the paths Wwise expects by convention. Renaming them
breaks WwiseCLI's project loader.

---

## Template Content

### `template.wproj`

Minimum required declarations:

- **Project name:** `template`
- **Schema / app version:** Wwise 2022.1 LTS (the current free-tier LTS).
  Wwise migrates older project schemas forward silently, so a 2022.1 template
  works with 2023.x and 2024.x without modification.
- **Platform:** Windows only. No iOS, Android, or other platforms.
- **Work unit reference:** one entry pointing to
  `Conversion Settings\Factory.wwu` as a persistent audio source settings
  work unit.
- Nothing else. No buses, no events, no actor-mixer hierarchy, no languages
  beyond what Wwise requires to open the file without errors.

### `Factory.wwu`

Defines exactly three named Vorbis conversion presets. All three force 48000 Hz
output regardless of source sample rate — encoding at the wrong rate would
cause the game's runtime to pitch-shift audio.

| Preset name | Vorbis quality factor | Output sample rate | Channels |
|---|---|---|---|
| `VorbisLow` | 0.3 | 48000 Hz (forced) | Match source |
| `VorbisMedium` | 0.6 | 48000 Hz (forced) | Match source |
| `VorbisHigh` | 0.9 | 48000 Hz (forced) | Match source |

"Match source" for channels is correct: source WAVs are expected to be mono
for story dialogue. No downmix setting is needed.

> **The exact XML for both files is a verification checkpoint.** The schema
> attribute names and element nesting differ across Wwise minor versions and
> cannot be reliably specified without round-tripping against a real install.
> The implementation step must author these files in Wwise 2022.1 directly:
> create a new project, configure the three presets as above, save, then strip
> everything except `template.wproj` and `Conversion Settings/Factory.wwu`.
> The semantics above are the complete requirement — no other content belongs
> in the template.

---

## `GetOrExtractTemplateWproj()` Changes

The method currently extracts a single file. It becomes a ZIP extraction.

```
private static string? _cachedWprojPath;

string GetOrExtractTemplateWproj()
    if _cachedWprojPath is not null → return it

    uri     = "avares://DialogEditor.Avalonia/Assets/Wwise/template.wwise.zip"
    destDir = Path.Combine(Path.GetTempPath(),
                           "PillarsDialogEditor", "wwise", "template")

    using var stream = AssetLoader.Open(uri)
    using var zip    = new ZipArchive(stream, ZipArchiveMode.Read)
    zip.ExtractToDirectory(destDir, overwriteFiles: true)

    _cachedWprojPath = Path.Combine(destDir, "template.wproj")
    return _cachedWprojPath
```

`destDir` is always fully overwritten on extraction — no stale-file check is
needed because the ZIP is baked into the app binary and cannot drift out of
sync with itself. The `static` cache means one extraction per app session
regardless of how many encodes run.

---

## Tests

Two new unit tests alongside the existing `GenerateWsourcesXml` tests in
`VoImporterTests`:

- **`GetOrExtractTemplateWproj_ExtractsWprojToExpectedPath`** — call once;
  assert the returned path ends with `template.wproj` and the file exists on
  disk.
- **`GetOrExtractTemplateWproj_ReturnsSamePathOnSecondCall`** — call twice;
  assert both return values are identical (cache hit verified, no second
  extraction).

---

## Verification Checkpoints (require real Wwise install)

These cannot be unit-tested:

1. **Template authoring** — create the two XML files in Wwise 2022.1 as
   described above. The implementation step must do this before the ZIP can
   be committed.

2. **End-to-end encode** — run `WwiseCLI.exe` with the template against a
   real WAV; confirm a `.wem` is produced at the expected output path
   (`{wprojDir}\GeneratedSoundBanks\Windows\{name}.wem` — exact path still
   to be confirmed against a real install per the encoding design).

3. **In-game playback** — patch the produced `.wem` into a vanilla Deadfire
   conversation and confirm it plays correctly. This resolves the open
   question of whether Wwise 2022.1-encoded files are compatible with the
   game's Wwise 2017.1.x runtime (see VO Integration Research doc).

Items 2 and 3 must pass before the template is considered shippable. If
in-game playback fails, the fallback is to target an earlier Wwise version
(e.g. 2019.2 LTS) and re-author the template.

---

## Open Questions

See `PoE Dialog Editor Research/Voice-Over Integration Research.md` for the
cross-version Vorbis compatibility question.
