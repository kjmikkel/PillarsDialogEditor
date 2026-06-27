# Wwise Template Asset Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single-file `template.wproj` placeholder with a properly structured `template.wwise.zip` containing a full minimal Wwise project folder, and update `GetOrExtractTemplateWproj()` to extract the ZIP instead of copying a single file.

**Architecture:** `VoImporter.GetOrExtractTemplateWproj()` is split into a private entry point (handles caching + AssetLoader) and a new `internal static ExtractTemplateZip(Stream, string?)` helper (handles ZIP extraction, testable without Avalonia). The embedded asset changes from `template.wproj` to `template.wwise.zip`. Task 2 is a manual Wwise authoring step that can only be done with a real Wwise 2022.1 install.

**Tech Stack:** C# / .NET 8, `System.IO.Compression` (already a BCL dependency — no new NuGet packages), Avalonia `AssetLoader`, xUnit.

## Global Constraints

- No user-visible text may be hardcoded; all strings go in `Strings.axaml` (no new strings in this feature).
- Every caught exception must be logged via `AppLog.Error` or `AppLog.Warn`; `OperationCanceledException` is swallowed silently.
- No bare `catch { }` blocks.
- Tests run serially: `dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false`.
- `Assets\**` glob in `DialogEditor.Avalonia.csproj` already picks up all files under `Assets/` as `<AvaloniaResource>` — no csproj change needed.

---

## File Map

| File | Change |
|---|---|
| `DialogEditor.Avalonia/Audio/VoImporter.cs` | Extract `ExtractTemplateZip` helper; update `GetOrExtractTemplateWproj()` to use ZIP |
| `DialogEditor.Tests/Audio/VoImporterTests.cs` | Add two tests for `ExtractTemplateZip` |
| `DialogEditor.Avalonia/Assets/Wwise/template.wwise.zip` | **Manual step (Task 2):** add real ZIP authored in Wwise 2022.1 |
| `DialogEditor.Avalonia/Assets/Wwise/template.wproj` | **Manual step (Task 2):** delete this placeholder once ZIP is committed |

---

## Task 1: Refactor extraction + add tests

**Files:**
- Modify: `DialogEditor.Avalonia/Audio/VoImporter.cs`
- Modify: `DialogEditor.Tests/Audio/VoImporterTests.cs`

**Interfaces:**
- Produces: `internal static string VoImporter.ExtractTemplateZip(Stream zipStream, string? destDir = null)`
  - Extracts the ZIP from `zipStream` into `destDir` (defaults to `%TEMP%\PillarsDialogEditor\wwise\template\` when null), overwrites existing files, returns the path to `template.wproj` inside the extracted folder.

---

- [ ] **Step 1: Write two failing tests**

Open `DialogEditor.Tests/Audio/VoImporterTests.cs` and add at the bottom of the class (inside the existing `VoImporterTests` class, after the last existing test):

```csharp
[Fact]
public void ExtractTemplateZip_CreatesWprojFile()
{
    using var ms = CreateStubZip();
    var destDir = Path.Combine(Path.GetTempPath(), $"VoImporterTest_{Guid.NewGuid():N}");
    try
    {
        var result = VoImporter.ExtractTemplateZip(ms, destDir);
        Assert.True(File.Exists(result));
    }
    finally
    {
        if (Directory.Exists(destDir))
            Directory.Delete(destDir, recursive: true);
    }
}

[Fact]
public void ExtractTemplateZip_ReturnsPathEndingWithTemplateWproj()
{
    using var ms = CreateStubZip();
    var destDir = Path.Combine(Path.GetTempPath(), $"VoImporterTest_{Guid.NewGuid():N}");
    try
    {
        var result = VoImporter.ExtractTemplateZip(ms, destDir);
        Assert.EndsWith("template.wproj", result, StringComparison.OrdinalIgnoreCase);
    }
    finally
    {
        if (Directory.Exists(destDir))
            Directory.Delete(destDir, recursive: true);
    }
}

// ── helpers ──────────────────────────────────────────────────────────────────

private static MemoryStream CreateStubZip()
{
    var ms = new MemoryStream();
    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
    {
        WriteEntry(archive, "template/template.wproj",              "<stub/>");
        WriteEntry(archive, "template/Conversion Settings/Factory.wwu", "<stub/>");
    }
    ms.Position = 0;
    return ms;
}

private static void WriteEntry(ZipArchive archive, string entryName, string content)
{
    var entry = archive.CreateEntry(entryName);
    using var writer = new StreamWriter(entry.Open());
    writer.Write(content);
}
```

Add the missing using at the top of the file if not already present:
```csharp
using System.IO.Compression;
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false --filter "FullyQualifiedName~ExtractTemplateZip"
```

Expected: both tests fail with `CS0117: 'VoImporter' does not contain a definition for 'ExtractTemplateZip'`.

- [ ] **Step 3: Implement `ExtractTemplateZip` and update `GetOrExtractTemplateWproj()`**

Open `DialogEditor.Avalonia/Audio/VoImporter.cs`. Replace the existing `GetOrExtractTemplateWproj()` method with:

```csharp
private static string GetOrExtractTemplateWproj()
{
    if (_cachedWprojPath is not null) return _cachedWprojPath;

    var uri = new Uri("avares://DialogEditor.Avalonia/Assets/Wwise/template.wwise.zip");
    using var stream = AssetLoader.Open(uri);
    _cachedWprojPath = ExtractTemplateZip(stream);
    return _cachedWprojPath;
}

/// <summary>
/// Extracts the Wwise project ZIP to a temp directory and returns the path to template.wproj.
/// Internal for testing — production callers use GetOrExtractTemplateWproj().
/// </summary>
internal static string ExtractTemplateZip(Stream zipStream, string? destDir = null)
{
    destDir ??= Path.Combine(
        Path.GetTempPath(), "PillarsDialogEditor", "wwise", "template");

    Directory.CreateDirectory(destDir);

    using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
    zip.ExtractToDirectory(destDir, overwriteFiles: true);

    return Path.Combine(destDir, "template.wproj");
}
```

Also update the `_cachedWprojPath` XML comment from `template.wproj` to `template.wwise.zip`:

```csharp
// Cached path of the template.wproj extracted from template.wwise.zip on first encode.
private static string? _cachedWprojPath;
```

Add `using System.IO.Compression;` to the top of the file if not already present.

- [ ] **Step 4: Run tests to confirm they pass**

```
dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false --filter "FullyQualifiedName~ExtractTemplateZip"
```

Expected: both new tests pass. Then run the full suite:

```
dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false
```

Expected: all tests pass (the existing `GenerateWsourcesXml` tests must still pass).

- [ ] **Step 5: Commit**

```
git add DialogEditor.Avalonia/Audio/VoImporter.cs
git add DialogEditor.Tests/Audio/VoImporterTests.cs
git commit -m "refactor(wwise): extract ZIP in GetOrExtractTemplateWproj + add ExtractTemplateZip tests"
```

---

## Task 2: Author template.wwise.zip in Wwise 2022.1 (manual)

**Files:**
- Create: `DialogEditor.Avalonia/Assets/Wwise/template.wwise.zip`
- Delete: `DialogEditor.Avalonia/Assets/Wwise/template.wproj`

**Prerequisite:** Wwise 2022.1 LTS installed (free Indie licence is sufficient).

This task cannot be automated. It must be done by hand in the Wwise Authoring application.

---

- [ ] **Step 1: Create a new Wwise project**

Open Wwise Authoring. File › New Project. Name: `template`. Choose any save location (e.g. Desktop). Accept all defaults.

- [ ] **Step 2: Remove all default content**

In the Project Explorer, delete everything under:
- Actor-Mixer Hierarchy
- Interactive Music Hierarchy
- Events
- Soundbanks
- Game Parameters
- Switches
- States
- Triggers

Leave only the **Conversion Settings** work unit — that is where the presets go.

- [ ] **Step 3: Add three Vorbis conversion presets**

In the Project Explorer, open **Conversion Settings › Default Work Unit** (or rename it to `Factory` — see note below).

Right-click › New Child › Conversion. Name it `VorbisLow`. In its properties:
- Codec: **Vorbis**
- Quality: **0.3**
- Sample rate: **48000 Hz** (force, do not use "Match source")
- Channels: use default (follow source)

Repeat for `VorbisMedium` (quality 0.6) and `VorbisHigh` (quality 0.9).

> **Note on work unit name:** The preset names (`VorbisLow`, `VorbisMedium`, `VorbisHigh`) must match exactly — they are referenced verbatim in `VoImporter.GenerateWsourcesXml()`. The work unit file name (`Factory.wwu`) is what `GetOrExtractTemplateWproj()` expects at `Conversion Settings/Factory.wwu`.

- [ ] **Step 4: Save and locate project files**

File › Save Project (Ctrl+S). Navigate to the project's save folder on disk. The folder should contain:

```
template/
  template.wproj
  Conversion Settings/
    Factory.wwu         ← must contain the three presets
  (other .wwu files for Actor-Mixer, Events, etc.)
```

- [ ] **Step 5: Strip to minimal files**

You only need `template.wproj` and `Conversion Settings/Factory.wwu`. Delete all other `.wwu` files and subfolders from the project folder. Verify Wwise can still open the stripped project by reopening it — it may warn about missing work units; confirm the conversion presets still appear under Conversion Settings.

> If Wwise refuses to open the stripped project, keep the minimal set of empty work units it complains about. The goal is the smallest project that WwiseCLI accepts for `-ConvertExternalSources`.

- [ ] **Step 6: Create the ZIP**

ZIP the `template/` folder (so the ZIP contains `template/template.wproj` and `template/Conversion Settings/Factory.wwu` at the root of the archive).

Rename the archive to `template.wwise.zip`.

Copy it to: `DialogEditor.Avalonia/Assets/Wwise/template.wwise.zip`

- [ ] **Step 7: Delete the placeholder**

Delete `DialogEditor.Avalonia/Assets/Wwise/template.wproj`.

- [ ] **Step 8: Verify end-to-end encode**

Run the Dialog Editor. Open a saved project that has a VO node. Click Import Voice-Over on any node. Browse for a `.wav` file. Set quality to Medium. Click OK.

Expected: `VoImporter.EncodeWavToWemAsync` runs without exception, a `.wem` file appears in the project's `_vo/` folder.

If WwiseCLI errors, check:
- The output path formula: `{wprojDir}\GeneratedSoundBanks\Windows\{destNameWithoutExtension}.wem` — update `EncodeWavToWemAsync` if Wwise writes to a different location.
- The `.wsources` schema version: if Wwise 2022.1 requires a different `SchemaVersion` attribute value, update `GenerateWsourcesXml`.

- [ ] **Step 9: Verify in-game playback**

Copy the produced `.wem` to a vanilla conversation's VO folder in the Deadfire install and launch the game. Trigger the line in-game.

Expected: audio plays correctly. If the game plays silence or crashes:
- This is the cross-version Vorbis compatibility issue flagged in the VO research doc.
- Fallback: re-author the template in Wwise 2019.2 LTS (an older but still-available version), which is closer to the game's 2017.1.x runtime.
- Mark the open question in `PoE Dialog Editor Research/Voice-Over Integration Research.md` resolved with findings.

- [ ] **Step 10: Commit**

```
git add "DialogEditor.Avalonia/Assets/Wwise/template.wwise.zip"
git rm  "DialogEditor.Avalonia/Assets/Wwise/template.wproj"
git commit -m "feat(wwise): add verified template.wwise.zip for WAV→WEM encoding"
```
