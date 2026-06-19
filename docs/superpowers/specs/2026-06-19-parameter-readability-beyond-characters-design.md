# Parameter Readability — Beyond Characters: Design

**Date:** 2026-06-19
**Status:** Approved
**Related gap:** Gaps.md — "Parameter Readability — Beyond Characters (PoE2 survey)"

---

## Problem

The condition and script editors show an `AutoCompleteBox` for any parameter whose `Type`
is `"ObjectGuid"` or `"Guid"`, populated with speaker names from `SpeakerNameService`. This
is wrong for every non-speaker `Guid` param: a `StartQuest` script's `Quest GUID` field
today offers a dropdown of NPC names. `"Guid"` is too coarse a type — it collapses quests,
items, abilities, maps, conversations, and ~20 other data kinds into the same bucket with
no way to distinguish them.

`"GlobalVariable"` and `"String"` params (flag names, PoE1 item/ability names) currently
render as plain `TextBox` controls with no suggestions at all.

---

## Goal

Every parameter that points at a known game-data kind — in both PoE1 and PoE2 — shows an
`AutoCompleteBox` backed by a lookup table for that kind. Free-text entry of raw GUIDs and
flag names is preserved. Parameters for unknown or unsupported kinds remain plain text
inputs.

---

## Approach: `LookupKind` annotation + `GameDataNameService` registry

### Catalogue annotation

Each parameter entry in `scripts.json` and `conditions.json` (editor resources, not game
files) gains an optional `"LookupKind"` string field naming the data kind it points at:

```json
{ "Name": "Quest GUID",  "Type": "Guid",           "LookupKind": "Quest"          }
{ "Name": "Item GUID",   "Type": "Guid",           "LookupKind": "Item"           }
{ "Name": "Tag",         "Type": "GlobalVariable", "LookupKind": "GlobalVariable" }
{ "Name": "Item Name",   "Type": "String",         "LookupKind": "Item"           }
{ "Name": "Speaker",     "Type": "ObjectGuid",     "LookupKind": "Speaker"        }
```

`ObjectGuid` speaker params are also annotated (`"LookupKind": "Speaker"`) so routing is
uniform across all 25 kinds. Parameters with no known lookup source leave `LookupKind`
absent or empty.

#### Full kind vocabulary (~25 values)

`Speaker`, `Quest`, `Item`, `Ability`, `GlobalVariable`, `Class`, `Race`, `Subrace`,
`Background`, `Culture`, `Deity`, `PaladinOrder`, `Faction`, `Disposition`,
`DispositionStrength`, `Skill`, `Phrase`, `Keyword`, `StatusEffect`, `CreatureType`,
`Map`, `Conversation`, `WeaponType`, `ArmorType`.

A build-time test enforces that every non-empty `LookupKind` value in the catalogues
matches a member of this whitelist (mirroring `NoStrayHexTests`). Typos (`"Qeust"`,
`"item"`) fail the build, not silently produce empty dropdowns.

---

### Data model

```csharp
// DialogEditor.ViewModels/Services/NamedEntry.cs
public record NamedEntry(string DisplayName, string StoredValue);
```

- **GUID kinds (PoE2):** `DisplayName = "Edér — 2c7f0b5a-…"`, `StoredValue = "2c7f0b5a-…"`.
  The `"Name — GUID"` format is preserved so `FilterMode=Contains` matches on both the
  readable name and a raw GUID fragment.
- **String kinds (GlobalVariable, PoE1 items/abilities):** `DisplayName = StoredValue =
  "npc_met_eder"` when only an internal name is available; or `DisplayName = "Sword of the
  Wael"` / `StoredValue = "Item_SwordWael"` when game data provides a separate readable name.

---

### `GameDataNameService`

```csharp
// DialogEditor.ViewModels/Services/GameDataNameService.cs
public static class GameDataNameService
{
    private static readonly Dictionary<string, IReadOnlyList<NamedEntry>> _registry = new();

    public static void Register(string kind, IReadOnlyList<NamedEntry> entries)
        => _registry[kind] = entries;

    public static IReadOnlyList<NamedEntry> Get(string kind)
        => _registry.TryGetValue(kind, out var e) ? e : [];

    public static void Clear() => _registry.Clear(); // test isolation only
}
```

Parsers call `Register` once per kind at game-folder-open time. A second `Register` for the
same kind replaces the previous entries (same contract as `SpeakerNameService.Register`).
`Get` on an unregistered kind returns an empty list — the AutoCompleteBox shows no
suggestions and the field behaves as a plain text input.

---

### `IGameDataProvider` extension

```csharp
void PopulateNameRegistry();
```

Called in the same game-folder-open code path as `LoadSpeakerNames`. Each game provider
(Poe1/Poe2) implements it, firing all its parsers and calling `GameDataNameService.Register`
for each supported kind — the same pattern as `SpeakerNameService.Register` today. Kinds a
game doesn't support are simply not registered.

`LoadSpeakerNames` is **retained** — `SpeakerNameService` is used throughout the codebase
for node display (conversation canvas, attribution panel). `PopulateNameRegistry` also
registers the `"Speaker"` kind into `GameDataNameService` from the same parsed data, so
`ParameterValueViewModel` can route uniformly.

---

### `ParameterValueViewModel` changes

Add `LookupKind` as an `init` property sourced from the catalogue JSON parameter entry.
Replace the `IsGuidType`/`GuidSuggestions` pair:

```csharp
public string LookupKind { get; init; } = string.Empty;

public bool HasLookup => LookupKind.Length > 0;

// IsText: was !IsEnum && !IsGuidType
public bool IsText => !IsEnum && !HasLookup;

public IReadOnlyList<string> Suggestions
    => HasLookup
       ? GameDataNameService.Get(LookupKind).Select(e => e.DisplayName).ToList()
       : [];
```

`IsGuidType` and `GuidSuggestions` are removed.

`OnValueChanged` normalisation changes from string-splitting to entry lookup:

```csharp
partial void OnValueChanged(string value)
{
    if (!HasLookup) return;
    var entry = GameDataNameService.Get(LookupKind)
        .FirstOrDefault(e => e.DisplayName == value);
    if (entry is not null)
        Value = entry.StoredValue;
}
```

This correctly handles both GUID kinds (extracts the GUID from the selected display string)
and string kinds (no-op when `DisplayName == StoredValue`) without any string manipulation.

---

### XAML changes (ScriptEditorWindow + ConditionEditorWindow)

The AutoCompleteBox `IsVisible` binding changes from `IsGuidType` to `HasLookup`; its
`ItemsSource` changes from `GuidSuggestions` to `Suggestions`. The `TextBox` `IsVisible`
changes from `IsText` (already derived from `!IsGuidType`) to the updated `IsText`
(`!IsEnum && !HasLookup`). No structural XAML changes.

---

## Parsers

### PoE2: generic bundle parser

All observed PoE2 gamedatabundles share this envelope:

```json
{ "GameDataObjects": [ { "ID": "…guid…", "DebugName": "SPK_Companion_Eder", … } ] }
```

A single `Poe2GameDataBundleParser` accepts a relative path (within the game data folder)
and an optional name-cleaning delegate; returns `IReadOnlyList<NamedEntry>`. The `"Speaker"`
kind's existing `SPK_`/category prefix-stripping rules are carried into this parser as the
default Speaker delegate.

All PoE2 kinds expected to follow this shape:
`Quest`, `Item`, `Ability`, `Class`, `Race`, `Subrace`, `Background`, `Culture`, `Deity`,
`PaladinOrder`, `Faction`, `Disposition`, `DispositionStrength`, `Skill`, `Phrase`,
`Keyword`, `StatusEffect`, `CreatureType`, `WeaponType`, `ArmorType`.

**Map** and **Conversation** are flagged as "verify during implementation" — map GUIDs may
come from an area bundle with a different structure; conversation GUIDs may be sourced from
the existing `*.conversationbundle` files already parsed by `Poe2ConversationParser`.

> **Implementation note:** Exact gamedatabundle filenames and field names beyond
> `speakers.gamedatabundle` must be confirmed against a real PoE2 game data folder before
> writing each parser. This is the expected first step of the implementation plan.

### PoE2: `GlobalVariables.csv`

`GlobalVariablesCsvParser` reads `GlobalVariables.csv` from the game data folder and
extracts the variable name column. `DisplayName = StoredValue = variableName`. A minimal
CSV reader — the `LocalizationExportService` establishes the project's CSV-handling pattern.

### PoE1

PoE1 string-typed params (item names, ability/talent names, quest names) store internal
identifiers, not GUIDs. Source files are expected to be XML assets in the `data/` tree
(the same tree the conversation parser navigates), but their exact filenames and schema
must be discovered against a real PoE1 game data folder during implementation.

PoE1 GlobalVariables: use the same `GlobalVariablesCsvParser` if the file exists; otherwise
no `"GlobalVariable"` entry is registered and the field remains plain text.

### File-not-found behaviour

Any parser that cannot locate its source file returns an empty list silently. The parameter
field renders as plain free-text input — identical to today's behaviour.

---

## Testing

### Catalogue annotation test

Extend catalogue-integrity tests: every non-empty `LookupKind` value in `scripts.json` /
`conditions.json` must match a member of the kind whitelist. Catches typos at build time.

### `GameDataNameService` unit tests

`Register` + `Get` round-trip; `Get` on unregistered kind returns empty; second `Register`
replaces first; `Clear` resets state.

### Parser unit tests

Each parser is tested with a small inline fixture (minimal JSON bundle string or CSV
fragment) — no real game folder required. The generic PoE2 bundle parser is tested with
two different fixture kinds to confirm parameterisation.

### `ParameterValueViewModel` unit tests

- `HasLookup` is true iff `LookupKind` is non-empty.
- `IsText` is false when `HasLookup` is true.
- `Suggestions` returns display names from whatever is registered in `GameDataNameService`
  for the given kind.
- `OnValueChanged` writes `StoredValue` when the selected `DisplayName` matches a known
  entry; leaves the raw value unchanged when it doesn't (free-text raw GUID / flag name
  preserved).

Tests seed `GameDataNameService` directly via `Register` — no parsers, no game folder.

### What is not unit-tested

PoE1 parser correctness against real game files — verified manually during the
implementation discovery step (same pragmatic stance as the existing speaker parsers).

---

## Gaps.md update

- Mark "Parameter Readability — Beyond Characters (PoE2 survey)" as `✅ IMPLEMENTED` once
  the plan ships.
- The original gap note about "first step is a survey" is satisfied by this design doc.
- Leave a deferred follow-up note for any PoE1 kinds that cannot be sourced (if game data
  files turn out not to exist or have unusable structure).
