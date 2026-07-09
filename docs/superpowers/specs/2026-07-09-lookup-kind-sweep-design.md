# Generic Bundle Sweep for Lookup Kinds — Design

**Date:** 2026-07-09
**Status:** Approved
**Gap:** The dormant-lookup-kinds follow-up recorded in `Gaps.md` › "Parameter Readability —
Beyond Characters" after the catalogue regeneration (B-011): the regenerated catalogue
emits ~53 lookup kinds, but `Poe2GameDataProvider` registers only ~20, so ~30 kinds
(Ship, Team, CharacterClass, Affliction, ShipUpgrade, Topic, Schedule, …) stay dormant —
their GUID parameters still display raw.
**Builds on:** `2026-07-07-catalogue-regeneration-design.md` (the generator whose
`$type → kind` mapping this mirrors), `2026-06-19-parameter-readability-beyond-characters-design.md`
(`GameDataNameService` and the existing per-kind loaders).

## Problem

Each catalogue parameter's `lookupKind` names a `GameDataNameService` registry. The
runtime populates those registries in `Poe2GameDataProvider.LoadGameDataNames()` via
hand-written per-kind registrations — one `Bundle(...)` line per kind, each naming its
bundle file and `$type` filter. That list predates the regenerated catalogue and covers
~20 kinds; the catalogue now references ~53. An unregistered kind is *safe* (empty
suggestions, raw GUID display) but useless — exactly the drift pattern that produced
B-011, recreated one level down.

**Survey (2026-07-09, shipped PoE2 bundles):** of the 32 dormant catalogue-referenced
kinds, **30 have a bundle-backed `$type`** named `<Kind>GameData` with usable `DebugName`s
— including `CreatureType` (110 objects in `characters.gamedatabundle`), which an earlier
hand survey wrongly recorded as having no source. Only `ProgressionUnlockable` and
`AttackBase` have no `$type` in any bundle; the generic `GameData` kind is unresolvable by
definition. Two bundle files nothing reads today hold several kinds:
`ships.gamedatabundle` (Ship, ShipUpgrade, ShipCaptain, ShipCrewPersonality, ShipDuelEvent,
ShipTriumph, ShipTrophy) and `ai.gamedatabundle` (Schedule).

## Decision (settled during brainstorming)

**Generic sweep, explicit-wins** — chosen over 30 hand-written registrations (recreates
the maintenance treadmill; parses files repeatedly) and over a catalogue-restricted sweep
(needs a wrong-direction Core→ViewModels dependency or a duplicated kind list).

One pass over every `*.gamedatabundle` buckets objects by `$type` and registers each
bucket under `TypeToKind($type)`. Registrations with genuine special handling run after
the sweep and overwrite it. Future catalogue kinds light up with zero code changes.

## Architecture

### 1. Sweep parser — `Poe2GameDataBundleParser.ParseAllByType`

```csharp
public static IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>>
    ParseAllByType(string path);
```

One pass over a bundle file's `GameDataObjects`, keyed by the **short** `$type`
(`Game.GameData.ShipGameData, Assembly-CSharp` → `ShipGameData`). Objects with an empty
`ID` or empty `DebugName` are skipped. Missing or unparseable files yield an empty
dictionary, matching `ParseFile`'s behaviour (no bare catch; same error-handling posture
as the existing parser).

### 2. Kind mapping — `GameDataKindMapper` (Core)

```csharp
public static string TypeToKind(string shortType);
```

Deliberately tiny, mirroring `tools/catalogue-gen/generate.py`'s `type_to_kind` exactly:

1. `BaseStatsGameData` → `Class`
2. `*ItemGameData` (any prefix) → `Item`
3. otherwise strip the trailing `GameData`; then alias `GenericAbility` → `Ability`

A comment on each side (C# and Python) points at the other; drift is guarded by unit
tests pinning all three rules plus the plain-strip case. The mapping exists twice because
the generator runs offline in Python against local-only inputs — sharing code is not
possible, so sharing a *three-rule contract* with cross-references and tests is the
containment.

### 3. Provider integration — `Poe2GameDataProvider.LoadGameDataNames`

Restructured into two phases:

**Phase 1 — sweep.** Enumerate every `*.gamedatabundle` under `design/gamedata`
(includes `ai.gamedatabundle` and `ships.gamedatabundle`, which nothing reads today).
For each file, `ParseAllByType`, map each `$type` through `TypeToKind`, and **merge**
entries per kind across files (e.g. `Item` spans multiple `*ItemGameData` types;
`Affliction` lives in `statuseffects.gamedatabundle`). Each file is parsed once —
today several files are parsed once per kind extracted from them.

**Phase 2 — explicit overrides (overwrite the sweep).** Only registrations with real
special handling survive as explicit code, assigning into the result dict after the sweep:

- `Disposition` — DebugName suffix strip (`"<Name>Disposition"` → `<Name>`)
- `PaladinOrder` — underscores → spaces
- `Class` — `BaseStatsGameData` filtered to `IsPlayerClass:"true"` (the sweep's unfiltered
  bucket would include NPC archetypes)
- `WeaponType` — name-only entries (conditions store DebugName, not GUID)

The plain explicit lines the sweep reproduces identically are **deleted**: Item, Ability,
StatusEffect, Phrase, Keyword, Map, Faction, Deity, ChangeStrength, Race, Subrace,
Background, Culture, Skill.

**Untouched:** Quest (`quests.questbundle`, own parser/path), GlobalVariable
(`GlobalVariables.csv`), Conversation (`.conversationbundle` enumeration), Speaker
(`LoadSpeakerNames`). PoE1's provider is unchanged (its catalogue entries use only
Speaker/GlobalVariable).

**Net effect:** all 30 sourceable dormant kinds register; `ProgressionUnlockable`,
`AttackBase`, and generic `GameData` stay dormant (no data source — safe, documented).
If a future game patch or overlooked bundle ever supplies a missing `$type`
(e.g. `ArmorTypeGameData`), it lights up automatically.

## Cross-cutting rules (CLAUDE.md)

- **Error handling** — the sweep parser mirrors `ParseFile`'s existing missing/corrupt-file
  behaviour; any caught exception is logged via `AppLog` per the house rule; no bare catch.
- **Localisation / tooltips / UI automation** — no UI changes; entries flow through the
  existing `GameDataNameService` → `ParameterValueViewModel` path.
- **Tests run serially** — new tests reuse the `Poe2GameDataProviderTests` temp-root
  fixture pattern and `GameDataNameService.Clear()` isolation where applicable.

## Testing (TDD, red first)

- **`ParseAllByType`:** fixture bundle with two `$type`s → grouped correctly; empty
  `ID`/`DebugName` objects skipped; missing file → empty dictionary.
- **`GameDataKindMapper`:** `BaseStatsGameData→Class`, `ConsumableItemGameData→Item`,
  `GenericAbilityGameData→Ability`, `ShipGameData→Ship` (plain strip).
- **Provider — sweep registers dormant kinds:** fixture `ships.gamedatabundle` →
  `Ship` kind contains the entry; fixture `ai.gamedatabundle` → `Schedule` registered.
- **Provider — explicit override wins:** a fixture `factions.gamedatabundle` with a
  `DispositionGameData` whose DebugName carries the `Disposition` suffix → the registered
  `Disposition` entries carry the *cleaned* name, not the sweep's raw one.
- **Regression:** the existing 13 `Poe2GameDataProviderTests` (incl. ChangeStrength) and
  the full suite stay green — the deleted plain registrations must be behaviourally
  replaced by the sweep.

## Docs

`Gaps.md`: update the dormant-kinds note under "Parameter Readability — Beyond
Characters" to "resolved by the generic sweep (30 kinds; ProgressionUnlockable/AttackBase
remain sourceless)", and correct the stale "no `CreatureTypeGameData` bundle found" claim
in the "Still deferred" list.

## Out of scope / deferred

- Kinds with no bundle `$type` (`ProgressionUnlockable`, `AttackBase`) and the generic
  `GameData` kind — dormant by necessity.
- Per-kind DebugName prettification beyond the existing four explicit overrides.
- Poe1 lookup expansion (its catalogue uses only Speaker/GlobalVariable).
- Any change to catalogue JSON, the generator, or the parameter-editor UI.
