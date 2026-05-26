# Localization Workflows — Design Spec

**Date:** 2026-05-26
**Status:** Approved

---

## Overview

Enables mod authors to export dialogue text for human translators, receive translated text back, and ship all languages inside a single `.dialogproject` file. All text — including the author's own language — lives exclusively in `ConversationPatch.Translations`; no language is privileged. When the patch is applied, translated stringtables are written for every language that is both present in the patch and installed on the user's machine.

Mod authors may also attach a **writer comment** to any node in a conversation — including unmodified vanilla nodes — to give translators context (tone, character state, length constraints, etc.). Comments are language-neutral, live in the project file, and are included in exports but ignored on import.

This introduces a **schema v2** for `ConversationPatch`. The application has not been distributed, so no migration is required and backwards compatibility with v1 patches is not needed.

---

## Architecture

Six components, one new enum, and changes to two existing classes:

```
DialogEditor.Core
  Models/NodeTranslation.cs              — new record (NodeId, DefaultText, FemaleText)
  GameData/IGameDataProvider.cs          — new GetStringTablePath(file, language) overload

DialogEditor.Patch
  ConversationPatch.cs                   — Translations + NodeComments added; text removed from AddedNodes/ModifiedNodes; CurrentSchemaVersion → 2; IsEmpty updated
  StringTableSerializer.cs              — new overload accepting IEnumerable<NodeTranslation>
  PatchApplier.cs                        — structural apply only; text handled by TranslationApplier
  TranslationApplier.cs                  — writes per-language stringtables for all installed languages
  LocalizationExportService.cs          — extracts text + comments from a project to CSV/JSON/XLIFF
  LocalizationImportService.cs          — reads translated file back into project patches
  LocalizationExportFormat.cs           — enum: Csv, Json, Xliff

DialogEditor.ViewModels
  ViewModels/MainWindowViewModel.cs      — ExportForTranslationCommand, ImportTranslationCommand
  Services/AppSettings.cs               — DefaultLocalizationFormat setting
  ViewModels/SettingsViewModel.cs        — LocalizationFormat property + combobox binding

DialogEditor.Avalonia
  Views/MainWindow.axaml                 — two new File menu items
  Views/LanguageCodeDialog.axaml+cs     — small modal: language code input
  Resources/Strings.axaml               — new string keys
```

Existing `dialog-patcher` CLI calls `TranslationApplier.WriteTranslations` after each `SaveConversation` — no other CLI changes needed.

---

## Section 1 — Data Model

### `NodeTranslation` (new record in `DialogEditor.Core/Models/`)

```csharp
public record NodeTranslation(int NodeId, string DefaultText, string FemaleText);
```

### `ConversationPatch` — schema v2

Text fields are removed from `NodeEditSnapshot` (used in `AddedNodes`) and from `NodeModification.FieldChanges`. All text, including the author's own language, lives exclusively in `Translations`. Writer comments live in `NodeComments`, keyed by node ID, covering any node in the conversation — not only nodes the patch structurally touches.

```csharp
public record ConversationPatch(
    string                          ConversationName,
    int                             SchemaVersion,
    IReadOnlyList<NodeEditSnapshot> AddedNodes,
    IReadOnlyList<int>              DeletedNodeIds,
    IReadOnlyList<NodeModification> ModifiedNodes)
{
    public static readonly int CurrentSchemaVersion = 2;

    // key = language code, e.g. "en", "fr", "de"
    // Contains at least one entry (the author's own language) when text has been edited.
    public IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>> Translations { get; init; }
        = new Dictionary<string, IReadOnlyList<NodeTranslation>>();

    // key = node ID; value = plain-text comment for translators.
    // May reference any node in the conversation, including unmodified vanilla nodes.
    public IReadOnlyDictionary<int, string> NodeComments { get; init; }
        = new Dictionary<int, string>();

    public bool IsEmpty =>
        AddedNodes.Count == 0 &&
        DeletedNodeIds.Count == 0 &&
        ModifiedNodes.Count == 0 &&
        Translations.Count == 0 &&
        NodeComments.Count == 0;
}
```

`NodeEditSnapshot` retains `DefaultText` / `FemaleText` as **in-memory-only** fields for the editor to display and edit, but these fields are **not serialised** into the patch file. When a patch is created from the editor state, text goes into `Translations[provider.Language]`, not into snapshot fields or `FieldChanges`.

`NodeModification.FieldChanges` may no longer contain `"DefaultText"` or `"FemaleText"` keys.

### Patch creation (existing code — change required)

When the editor builds a `ConversationPatch` from the current editing session:
- Text changes (new or modified `DefaultText`/`FemaleText`) are accumulated into `Translations[provider.Language]`, covering every node that was added or had its text modified.
- `AddedNodes` snapshots and `ModifiedNodes.FieldChanges` carry only structural fields (speaker, links, conditions, scripts, etc.).
- `NodeComments` entries are written whenever the user saves a comment on any node, regardless of whether that node has structural changes.

---

## Section 2 — Patch Application

### `IGameDataProvider` — new method

```csharp
string GetStringTablePath(ConversationFile file, string language);
```

Both `Poe1GameDataProvider` and `Poe2GameDataProvider` implement it by substituting `language` into their existing path formula (same logic as the parameterless overload, but with an explicit language instead of `this.Language`).

### `PatchApplier` — structural apply only

`PatchApplier` applies structural changes (node additions, deletions, link/speaker/condition/script modifications) and calls `provider.SaveConversation`. `SaveConversation` writes **structure only** — it no longer writes or reads the stringtable for the active language. Text is handled entirely by `TranslationApplier`. `NodeComments` are not applied to game files — they are project-file-only data.

### `StringTableSerializer` — new overload

```csharp
public static void SaveToFile(string path, IEnumerable<NodeTranslation> translations);
```

Writes the same XML format as the existing overload, using `NodeTranslation.NodeId`, `.DefaultText`, `.FemaleText`.

### `TranslationApplier` (new class in `DialogEditor.Patch/`)

Writes stringtable files for every language that is both present in `patch.Translations` and installed on the user's machine. No language is treated specially.

```csharp
public static class TranslationApplier
{
    public static void WriteTranslations(
        ConversationFile file,
        ConversationPatch patch,
        IGameDataProvider provider)
    {
        if (patch.Translations.Count == 0) return;
        var installed = provider.AvailableLanguages.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (lang, translations) in patch.Translations)
        {
            if (!installed.Contains(lang)) continue;
            var stPath = provider.GetStringTablePath(file, lang);
            Directory.CreateDirectory(Path.GetDirectoryName(stPath)!);
            StringTableSerializer.SaveToFile(stPath, translations);
        }
    }
}
```

Call sites: `dialog-patcher` CLI and the editor's apply path both call `WriteTranslations` immediately after `provider.SaveConversation`.

---

## Section 3 — Export Service

New `LocalizationExportService` static class in `DialogEditor.Patch/`.

### What gets exported

For each `ConversationPatch` in the project, read from `patch.Translations[sourceLanguage]`:
- Every `NodeTranslation` in the source-language entry becomes one export row.
- The writer comment for that node (from `patch.NodeComments`) is included on the same row. Empty string if no comment exists.

If a node already has an entry in another language in `Translations`, the existing translated text is pre-populated in the corresponding output column (allowing re-export after partial translation without losing prior work). Nodes absent from the source-language entry are excluded.

### Entry point

```csharp
public static class LocalizationExportService
{
    public static void Export(
        DialogProject project,
        string outputPath,
        LocalizationExportFormat format,
        string sourceLanguage);
}
```

`sourceLanguage` is the language whose text populates the source columns. The UI defaults it to `provider.Language` (the editor's current language).

### Formats

**CSV** — RFC 4180, one row per node, header row:
```
ConversationName,NodeId,WriterComment,SourceDefaultText,SourceFemaleText,TranslatedDefaultText,TranslatedFemaleText
```

**JSON** — top-level object; both `sourceLanguage` and `targetLanguage` are recorded:
```json
{
  "sourceLanguage": "en",
  "targetLanguage": "",
  "entries": [
    {
      "conversation": "...",
      "nodeId": 0,
      "writerComment": "...",
      "sourceDefaultText": "...",
      "sourceFemaleText": "...",
      "translatedDefaultText": "",
      "translatedFemaleText": ""
    }
  ]
}
```

**XLIFF 1.2** — one `<file>` per conversation, one `<trans-unit>` per node. `DefaultText` is the unit source/target. Two `<note>` elements are used when both are present: `<note from="female">` for the female variant text, `<note from="writer">` for the writer comment. `source-language` and `target-language` attributes are set; `target-language` is empty until filled by a CAT tool or the user.

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
3. Entries with both `TranslatedDefaultText` and `TranslatedFemaleText` empty are excluded (untranslated).
4. Entries referencing a conversation not in the project are silently ignored.
5. `WriterComment` / `<note from="writer">` fields in the import file are **ignored** — comments flow from author to translator, not back.
6. Return a new `DialogProject` (immutable update via `with`).

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
2. `LanguageCodeDialog` opens pre-populated with `provider.Language` as the source language (user can change).
3. `LocalizationExportService.Export` runs.
4. Status bar shows success message or logs error via `AppLog.Error`.

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

- `LocalizationExportServiceTests` — export produces correct CSV/JSON/XLIFF from a project with known patches; writer comments appear on the correct rows; nodes with no comment export an empty comment field; `sourceLanguage` absent from `Translations` produces no rows; pre-existing translations in a second language are pre-populated in output
- `LocalizationImportServiceTests` — round-trip (export then import) preserves all text; writer comments in the import file are ignored (not written back to `NodeComments`); partial translation (some rows empty) skips empty entries; unknown conversation silently ignored; language parameter stored correctly on the patch; importing the author's own language works identically to any other language
- `TranslationApplierTests` — `WriteTranslations` writes the correct file at the correct language path; empty `Translations` writes nothing; multiple languages each get their own file; languages absent from `provider.AvailableLanguages` are skipped; the author's own language is written through the same path as any other language
- `ConversationPatchSerializationTests` — v2 patch with `Translations` and `NodeComments` round-trips through JSON correctly; `AddedNodes` and `ModifiedNodes` contain no text fields in the serialised form; patch with only `NodeComments` entries is not considered empty

---

## TDD Order

1. `ConversationPatch` schema v2 serialisation tests (`Translations` and `NodeComments` round-trip; no text in `AddedNodes`/`ModifiedNodes`; `IsEmpty` updated) → update `ConversationPatch` and patch creation logic
2. `StringTableSerializer` new overload tests → implement
3. `IGameDataProvider.GetStringTablePath(file, lang)` + `TranslationApplierTests` → implement
4. `LocalizationExportServiceTests` → implement (CSV first, then JSON, then XLIFF)
5. `LocalizationImportServiceTests` → implement
6. Wire up CLI, commands, and Avalonia UI last (no unit tests for UI layer)
