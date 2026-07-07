# Script/Condition Catalogue Regeneration — Design

**Date:** 2026-07-07
**Status:** Approved
**Gap:** Systemic catalogue incompleteness (discovered via B-010 follow-up). The hand-authored
`scripts.json` (37 entries) / `conditions.json` (164 entries) cover only ~77 of the 353
distinct method signatures used across the 1,130 shipped PoE2 conversations — 276 are missing,
so any missing method with GUID parameters renders those parameters as raw GUIDs with no lookup
assistance. (Reported case: node 177 of `08_cv_atsura`, `ReputationAddPoints(Guid, Axis, Guid)`.)
**Builds on:** `2026-06-19-parameter-readability-beyond-characters-design.md` (the `lookupKind` /
`GameDataNameService` machinery this reuses and extends).

## Problem

`scripts.json` and `conditions.json` are the hand-authored catalogue that maps a conversation's
`FullName`-keyed script/condition calls to display names, categories, parameter names/types, and —
crucially — `lookupKind` annotations that turn a raw GUID parameter into a named-entry
autocomplete. The catalogue was built by hand and covers a small curated subset. `ScriptRowViewModel`
takes a **nullable** catalogue entry, so any call whose signature is absent falls back to raw
parameter display: string/int params look fine, but GUID params show as bare GUIDs with no help.

Measured coverage: of **353** distinct `FullName` signatures used across shipped PoE2 conversations,
**276 are absent** from the catalogue. This is systemic, not a one-off mislabel. The decompiled game
sources, however, carry rich machine-readable metadata for **every** method (434 scripts, 393
conditions in PoE2; PoE1 similar), so the catalogue can be regenerated from that ground truth.

## Decisions (settled during brainstorming)

- **Systemic regeneration**, not per-method patching.
- **Committed reproducible generator** (not a throwaway) so the catalogue can be re-derived when
  the game patches or annotations need correcting. The generated JSON is also committed.
- **Both games, full regen:** parse PoE1 and PoE2 decompiled sources, merge by signature (union
  `games`), regenerate wholesale. The generator reproduces prior hand-fixes from source (e.g. the
  B-010 Faction lookup falls out of the PoE2 `IsReputation`/`ReputationRank*` attributes). Trades
  hand-polished descriptions for the game's own attribute text.
- **Emit specific lookup kinds even without a runtime loader.** `GameDataNameService.Get` returns
  empty for an unregistered kind (verified by `GameDataNameServiceTests`), so a kind with no loader
  is harmless (raw GUID + empty dropdown) and lights up automatically when a loader is later added.

## Source metadata (ground truth)

Every method in the decompiled `Scripts.cs` / `Conditionals.cs` carries attributes:

```csharp
[Script("Add Reputation", "Scripts\\Faction")]                       // display, category
[ScriptParam0("Faction", "Faction to modify.", "", "e7b2bb2a-…", BrowserType.GameData)]
[ScriptParam1("Axis", "Good vs. Bad action", Axis.Positive)]         // enum + default
[ScriptParam2("Strength", "…", "71c858fe-…", "32e5c672-…", BrowserType.GameData)]
public static void ReputationAddPoints(Guid factionGuid, Axis axis, Guid strengthGuid)
```

Conditions mirror this with `[ConditionalScript("Display", "Conditionals\\Category")]`. The
`BrowserType` enum is `{None, GlobalVariable, Conversation, Quest, ObjectGuid, GameData,
GlobalScript, GlobalConditional, GlobalPreference, Chatter}`. PoE1 uses the same attribute grammar
with simpler params (mostly strings/enums; few GUID lookups).

## Architecture

### 1. Generator — `tools/catalogue-gen/` (Python, committed)

Run manually with local paths to the decompiled sources and the exported game bundles (all of which
live only on the dev machine, not in CI). Parses the regular attribute grammar with regex (no Roslyn
needed) and emits `scripts.json` + `conditions.json`, writing **both** the embedded copy
(`DialogEditor.ViewModels/Resources/`) and the `data/` mirror so the two cannot drift.

Per method it produces one catalogue entry:
- `[Script/ConditionalScript(display, "X\\Category")]` → `displayName`, `category` (the segment after `\\`).
- The C# signature → `methodName`, `fullName` (reflection form, e.g. `Void ReputationAddPoints(Guid, Axis, Guid)`), and each parameter's CLR type.
- `[ScriptParamN(name, desc, default…, BrowserType.X)]` + the Nth CLR type → one parameter object.
- Enum-typed params → `type: "Enum:<EnumName>"` with `options` taken from the enum's declared members (enum definitions parsed from the same sources).

**Both-games merge:** parse PoE1 and PoE2; key entries by `fullName`; union `games` when a signature
appears in both. Distinct signatures (PoE1 `ReputationAddPoints(FactionName,…)` vs PoE2
`(Guid,Axis,Guid)`) stay separate entries, each with its own `games`.

**Schema preserved exactly** (so `ConditionCatalogue`/`ScriptCatalogue` and all C# consumers are
untouched):
`methodName, fullName, displayName, category, games, description, parameters[{name, type,
description, default, options, values, lookupKind}]`.

### 2. lookup-kind inference

`BrowserType` → `lookupKind`:

| `BrowserType` | `lookupKind` |
|---|---|
| `None` | *(none — plain text, or enum via `type`)* |
| `GlobalVariable` | `GlobalVariable` |
| `Conversation` | `Conversation` |
| `Quest` | `Quest` |
| `ObjectGuid` | `Speaker` |
| `Chatter` | `Speaker` |
| `GlobalScript` / `GlobalConditional` / `GlobalPreference` | *(none)* |
| `GameData` | resolved by default-GUID → `$type` (below) |

**`GameData` resolution:** the generator scans all bundles once to build a `GUID → $type` index and
a `$type → kind` map (derived from the `$type` names actually seen, e.g. `FactionGameData → Faction`,
`ChangeStrengthGameData → ChangeStrength`, `AbilityGameData → Ability`, `*ItemGameData → Item`,
`CharacterClassGameData → Class`, `StatusEffectGameData → StatusEffect`, …). For each `GameData`
param it resolves the param's default GUID → `$type` → `kind`.
- Default GUID empty or unresolvable → emit generic `lookupKind: "GameData"` (dormant; intent kept).
- Resolvable → emit the specific kind, **whether or not a runtime loader exists yet** (safe;
  dormant kinds light up when a loader is added).

Worked: `ReputationAddPoints(Guid, Axis, Guid)` → param0 `Faction` (loader exists → works now),
param1 `Enum:Axis` `[Positive, Negative]`, param2 `ChangeStrength` (new kind; dormant until Phase 2).

### 3. Runtime coverage — `Poe2GameDataProvider`

The generator's `$type → kind` set is the complete list of kinds the catalogue references. Diff it
against the kinds `Poe2GameDataProvider.LoadGameDataNames()` already registers (Faction, Disposition,
Item, Ability, StatusEffect, Phrase, Keyword, Map, Skill, WeaponType, Quest, GlobalVariable,
Conversation, Class, Speaker, …) and add a loader for each gap that has a bundle source — e.g.
`FactionBundle("ChangeStrength", "ChangeStrengthGameData")` (in `factions.gamedatabundle`; DebugNames
"Major"/"Minor"/…). Kinds with no bundle source remain dormant. `LookupKindWhitelistTests`' whitelist
expands to the full emitted kind set.

### 4. Regression guard — coverage fixture + test

The generator also emits a committed fixture `DialogEditor.Tests/Fixtures/catalogue-usage.txt`:
every distinct script/condition `fullName` used across all shipped conversations (extracted from the
bundles once and checked in, so CI needs no game install). A new test
(`CatalogueCoverageTests`) asserts the catalogue contains every signature in the fixture. **This test
would have caught the original bug** and pins coverage against future drift. A `lookupKind`
whitelist/typo test and a catalogue-count golden guard round it out.

## Cross-cutting rules (CLAUDE.md)

- **Localisation** — the catalogue is data, not UI chrome; display strings come from the game's own
  attribute text (English). This matches the existing catalogue (already English data). No new
  hardcoded UI strings are introduced in C#/XAML.
- **Error handling** — the generator is a dev tool (outside the app); it fails loudly on
  unparseable input. Runtime code paths are unchanged (schema preserved) and already guard missing
  entries via the nullable-entry fallback; no bare catch added.
- **UI Automation / tooltips** — no UI controls change; the parameter editors already render from the
  catalogue schema.
- **Tests run serially** — new tests use the same `GameDataNameService.Clear()`/isolation patterns as
  existing catalogue tests; no new global-state coupling.

## Testing (TDD, red first)

**Generator (Python unit tests, in `tools/catalogue-gen/`):**
- Attribute parsing: a sample method snippet → the expected entry (methodName/fullName/display/category).
- Parameter extraction incl. description/default and `BrowserType → lookupKind` for each browser type.
- Enum-option extraction: an enum definition → the `options` list; param `type` becomes `Enum:<Name>`.
- `GameData` resolution: a tiny bundle + a default GUID → the specific kind; unresolvable → `GameData`.
- Both-games merge: same signature in both sources → one entry with `games:["poe1","poe2"]`; distinct
  signatures → two entries.

**C# side:**
- `CatalogueCoverageTests` (red first): fails while the current catalogue misses fixture signatures;
  passes after regeneration.
- `LookupKindWhitelistTests`: whitelist updated to the regenerated kind set (no typos).
- `Poe2GameDataProviderTests`: each newly-added loader resolves a known entry (e.g. `ChangeStrength`
  → "Major").
- Existing `ConditionCatalogue`/`ScriptCatalogue` tests remain green (schema unchanged); count/golden
  guards updated to the regenerated totals.

## Phasing (for the implementation plan)

1. **Generator + regenerated JSON + coverage fixture/test.** Fixes raw-GUID display for every method
   whose lookup kinds already have loaders (the majority), and establishes the coverage guard.
2. **Runtime loaders for newly-surfaced kinds** (e.g. `ChangeStrength`), lighting up the stragglers.

Phase 1 delivers the bulk of the value; Phase 2 is incremental and low-risk.

## Out of scope / deferred

- Hand-polishing generated descriptions beyond the game's attribute text.
- Lookup kinds whose GameData type has no bundle source (remain dormant, as today for ArmorType/CreatureType).
- Any change to the parameter-editor UI or the catalogue JSON schema.
- Generic scene-object GUID resolution for `ObjectGuid` params (still mapped to `Speaker`).
