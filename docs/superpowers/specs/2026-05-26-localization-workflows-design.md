# Localization Workflows — Design Spec

**Date:** 2026-05-26
**Status:** Approved

---

## Overview

Enables mod authors to export dialogue text for human translators, receive translated text back, and ship all languages inside a single `.dialogproject` file. When the patch is applied (via the editor or `dialog-patcher` CLI), translated stringtables are written to every language directory present in the patch.

---

## Architecture

Six components, one new enum:

```
DialogEditor.Core
  Models/NodeTranslation.cs          — new record (NodeId, DefaultText, FemaleText)
  GameData/IGameDataProvider.cs      — new GetStringTablePath(file, language) overload
  Serialization/StringTableSerializer.cs — new overload accepting IEnumerable<NodeTranslation>

DialogEditor.Patch
  ConversationPatch.cs               — new Translations property
  TranslationApplier.cs              — writes per-language stringtables after patch apply
  LocalizationExportService.cs       — extracts text from a project to CSV/JSON/XLIFF
  LocalizationImportService.cs       — reads translated file back into project patches
  LocalizationExportFormat.cs        — enum: Csv, Json, Xliff

DialogEditor.ViewModels
  ViewModels/MainWindowViewModel.cs  — ExportForTranslationCommand, ImportTranslationCommand
  Services/AppSettings.cs            — DefaultLocalizationFormat setting
  ViewModels/SettingsViewModel.cs    — LocalizationFormat property + combobox binding

DialogEditor.Avalonia
  Views/MainWindow.axaml             — two new File menu items
  Views/LanguageCodeDialog.axaml+cs  — small modal: language code input
  Resources/Strings.axaml            — new string keys
```

Existing `dialog-patcher` CLI calls `TranslationApplier.WriteTranslations` after each `SaveConversation` — no other CLI changes needed.

---

## Section 1 — Data Model

### `NodeTranslation` (new record in `DialogEditor.Core/Models/`)

```csharp
public record NodeTranslation(int NodeId, string DefaultText, string FemaleText);
```

### `ConversationPatch` — new `Translations` property

Non-positional so existing serialised patches deserialise cleanly (value is `null` when absent):

```csharp
public record ConversationPatch(
    string                          ConversationName,
    int                             SchemaVersion,
    IReadOnlyList<NodeEditSnapshot> AddedNodes,
    IReadOnlyList<int>              DeletedNodeIds,
    IReadOnlyList<NodeModification> ModifiedNodes)
{
    public static readonly int CurrentSchemaVersion = 1;

    // key = language code, e.g. "fr", "de"
    public IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>>? Translations { get; init; }

    public bool IsEmpty => ...  // unchanged
}
```

Each `NodeTranslation` entry covers one node for one language. An entry only exists for nodes the patch adds or changes text on.

---

## Section 2 — Patch Application

### `IGameDataProvider` — new method

```csharp
string GetStringTablePath(ConversationFile file, string language);
```

Both `Poe1GameDataProvider` and `Poe2GameDataProvider` implement it by substituting `language` into their existing path formula (same logic as the parameterless overload, but with an explicit language instead of `this.Language`).

### `StringTableSerializer` — new overload

```csharp
public static void SaveToFile(string path, IEnumerable<NodeTranslation> translations);
```

Writes the same XML format as the existing overload, using `NodeTranslation.NodeId`, `.DefaultText`, `.FemaleText`.

### `TranslationApplier` (new class in `DialogEditor.Patch/`)

```csharp
public static class TranslationApplier
{
    public static void WriteTranslations(
        ConversationFile file,
        ConversationPatch patch,
        IGameDataProvider provider)
    {
        if (patch.Translations is null) return;
        foreach (var (lang, translations) in patch.Translations)
        {
            var stPath = provider.GetStringTablePath(file, lang);
            Directory.CreateDirectory(Path.GetDirectoryName(stPath)!);
            StringTableSerializer.SaveToFile(stPath, translations);
        }
    }
}
```

Call sites: `dialog-patcher` CLI and the editor's test-patch path both call `WriteTranslations` immediately after `provider.SaveConversation`. `PatchApplier` itself is unchanged.

---

## Section 3 — Export Service

New `LocalizationExportService` static class in `DialogEditor.Patch/`.

### What gets exported

For each `ConversationPatch` in the project:
- **Added nodes:** `patch.AddedNodes[i].DefaultText` / `.FemaleText`
- **Modified nodes:** `mod.FieldChanges["DefaultText"].To` and `["FemaleText"].To` — only if those fields are present in the modification

Each entry: `ConversationName`, `NodeId`, `SourceDefaultText`, `SourceFemaleText`, empty `TranslatedDefaultText`, empty `TranslatedFemaleText`.

### Entry point

```csharp
public static class LocalizationExportService
{
    public static void Export(
        DialogProject project,
        string outputPath,
        LocalizationExportFormat format);
}
```

### Formats

**CSV** — RFC 4180, one row per node, header row:
```
ConversationName,NodeId,SourceDefaultText,SourceFemaleText,TranslatedDefaultText,TranslatedFemaleText
```

**JSON** — top-level object; `targetLanguage` starts empty for the translator to fill in:
```json
{
  "targetLanguage": "",
  "entries": [
    {
      "conversation": "...",
      "nodeId": 0,
      "sourceDefaultText": "...",
      "sourceFemaleText": "...",
      "translatedDefaultText": "",
      "translatedFemaleText": ""
    }
  ]
}
```

**XLIFF 1.2** — one `<file>` per conversation, one `<trans-unit>` per node. `DefaultText` is the unit source/target. Non-empty `FemaleText` is added as a `<note>`. `target-language` attribute is empty until filled by a CAT tool or the user.

### `LocalizationExportFormat` enum (new file in `DialogEditor.Patch/`)

```csharp
public enum LocalizationExportFormat { Csv, Json, Xliff }
```

---

## Section 4 — Import Service

New `LocalizationImportService` static class in `DialogEditor.Patch/`.

### Entry point

```csharp
public static class LocalizationImportService
{
    public static DialogProject Import(
        DialogProject project,
        string inputPath,
        LocalizationExportFormat format,
        string language);
}
```

`language` is always provided by the caller (from user input). For JSON, the file's `targetLanguage` field is offered as a default in the UI but the user can override. For XLIFF, the `target-language` attribute serves the same purpose.

### Logic

1. Read the file and group entries by `ConversationName`.
2. For each `ConversationPatch` in the project whose name matches, set or replace `patch.Translations[language]` with the imported `NodeTranslation` list.
3. Entries with both `TranslatedDefaultText` and `TranslatedFemaleText` empty are excluded (untranslated — no point storing empty strings).
4. Entries referencing a conversation not in the project are silently ignored.
5. Return a new `DialogProject` (immutable update via `with`).

After import the project is marked dirty — the user must save to write the translations into the `.dialogproject` file.

---

## Section 5 — UI and Settings

### File menu additions (`MainWindow.axaml`)

```xml
<Separator/>
<MenuItem Header="{StaticResource Menu_ExportForTranslation}"
          Command="{Binding ExportForTranslationCommand}"
          ToolTip.Tip="{StaticResource ToolTip_ExportForTranslation}"/>
<MenuItem Header="{StaticResource Menu_ImportTranslation}"
          Command="{Binding ImportTranslationCommand}"
          ToolTip.Tip="{StaticResource ToolTip_ImportTranslation}"/>
```

Both commands are disabled (`CanExecute = false`) when no project is open.

### Export flow

1. File save dialog filtered to the default format (from `AppSettings.DefaultLocalizationFormat`). All three formats are always available as additional filters.
2. `LocalizationExportService.Export` runs.
3. Status bar shows success message or logs error via `AppLog.Error`.

### Import flow

1. File open dialog (all three extensions accepted: `.csv`, `.json`, `.xlf`/`.xliff`).
2. `LanguageCodeDialog` opens — a small modal with a single text field ("Language code, e.g. fr, de, ja") pre-populated from the file if the format embeds it.
3. On confirm, `LocalizationImportService.Import` runs.
4. Project is marked dirty. Status bar confirms import (e.g., "Imported 47 translations for fr").

### `LanguageCodeDialog`

Minimal modal window (Width=340, Height=160): title, label, single-line text input, OK/Cancel buttons. Returns `string?` (null on cancel). Follows the existing pattern of small dialogs in the project (e.g., the merge conflict dialog). Must carry the app icon per project convention.

### Settings

`AppSettings` gains `DefaultLocalizationFormat` (serialised as `"Csv"`, `"Json"`, or `"Xliff"`; default `"Csv"`).

`SettingsViewModel` exposes `LocalizationFormat` as a string property bound to a `ComboBox` in `SettingsWindow` with the three options.

### String keys (`Strings.axaml`)

```
Menu_ExportForTranslation
Menu_ImportTranslation
ToolTip_ExportForTranslation
ToolTip_ImportTranslation
Localization_LanguageCodeDialog_Title
Localization_LanguageCodeDialog_Prompt
Localization_StatusExported        — "{0} entries exported to {1}"
Localization_StatusImported        — "{0} translations imported for {1}"
Localization_StatusImportNoEntries — "No translated entries found in file"
Settings_LocalizationFormat
```

---

## Testing

- `LocalizationExportServiceTests` — export produces correct CSV/JSON/XLIFF from a project with known patches; empty patch produces no rows; modified-text-only nodes included; structural-only modifications (no text fields) excluded
- `LocalizationImportServiceTests` — round-trip (export then import) preserves all text; partial translation (some rows empty) skips empty entries; unknown conversation silently ignored; language parameter stored correctly on the patch
- `TranslationApplierTests` — `WriteTranslations` writes the correct file at the correct language path; null `Translations` writes nothing; multiple languages each get their own file
- `ConversationPatchSerializationTests` — patch with `Translations` round-trips through JSON correctly; patch without `Translations` deserialises with null (backwards compatibility)

---

## TDD Order

1. `NodeTranslation` + `ConversationPatch.Translations` serialisation tests → implement
2. `StringTableSerializer` new overload tests → implement
3. `IGameDataProvider.GetStringTablePath(file, lang)` + `TranslationApplierTests` → implement
4. `LocalizationExportServiceTests` → implement (CSV first, then JSON, then XLIFF)
5. `LocalizationImportServiceTests` → implement
6. Wire up CLI, commands, and Avalonia UI last (no unit tests for UI layer)
