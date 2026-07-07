# Script/Condition Catalogue Regeneration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Regenerate `scripts.json` / `conditions.json` from the decompiled game sources so the catalogue covers every script/condition signature used in shipped conversations, with correct per-parameter lookup kinds — eliminating raw-GUID parameter display (e.g. node 177 `ReputationAddPoints(Guid, Axis, Guid)`).

**Architecture:** A committed Python generator (`tools/catalogue-gen/`) parses the attribute grammar in the decompiled `Scripts.cs` / `Conditionals.cs` (PoE1 + PoE2), resolves enum option lists and `GameData` parameter kinds (via each param's default-GUID → bundle `$type`), merges by signature across games, and emits the two JSON files (embedded copy + `data/` mirror) plus a coverage fixture. A new C# coverage test pins the catalogue against signatures actually used in shipped conversations. Runtime lookup loaders are extended for any newly-surfaced kinds (Phase 2).

**Tech Stack:** Python 3 (generator + its tests), C# / .NET / xUnit (app + coverage/whitelist tests). Inputs are local-only: decompiled sources under `…/Deadfire/PoE1 Code` & `PoE2 Code`, and the exported bundles under the GOG install.

## Global Constraints

- **TDD, red first** for the C# side and the generator's Python units.
- **JSON schema is frozen** — the generator must emit exactly the existing shape so no C# consumer changes: `methodName, fullName, displayName, category, games, description, parameters[{name, type, description, default, options?, values?, lookupKind?}]`. Omit `options`/`values`/`lookupKind` when empty (matches current files; `TagCatalogue`/`ConditionCatalogue` use `JsonIgnoreCondition.WhenWritingNull` and case-insensitive names).
- **`fullName` fidelity is critical** — it is the runtime match key against conversation data. CLR type spelling in `fullName`: `void→Void`, `int→Int32`, `bool→Boolean`, `float→Single`, `string→String`, `System.Guid→Guid`; enums and other types by their **short** name as written (`Axis`, `Rank`, `RankType`, `Operator`, `Relationship`, `Subrace`, `MovementType`, `PointLocation`, `EncounterScenarioType`, `RelationshipThreshold`, …). Reflection form: `<ReturnType> <MethodName>(<Type1>, <Type2>, …)`, e.g. `Void ReputationAddPoints(Guid, Axis, Guid)`, `Boolean IsReputation(Guid, RankType, Int32, Operator)`.
- **Two JSON copies stay byte-identical:** `DialogEditor.ViewModels/Resources/{scripts,conditions}.json` (embedded) and `data/{scripts,conditions}.json` — the generator writes both.
- **Both games merged by `fullName`**, union `games`; distinct signatures stay separate entries.
- **Localisation:** catalogue is English data (as today); no new hardcoded C#/XAML UI strings.
- **Tests run serially;** reuse `GameDataNameService.Clear()` isolation in new tests.
- **Build/test:** `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`. Generator tests: `python tools/catalogue-gen/test_generate.py` (plain asserts; no pytest dependency assumed).
- **Local input paths (author machine):**
  - PoE2 scripts: `C:\Users\kjmik\Documents\Programming\Deadfire\PoE2 Code\Assembly-CSharp\Game\Scripts.cs`
  - PoE2 conditions: `…\PoE2 Code\Assembly-CSharp\Game\Conditionals.cs`
  - PoE1 scripts: `…\PoE1 Code\Assembly-CSharp\Scripts.cs` (conditions file: locate under `PoE1 Code`)
  - Bundles: `D:\Program Files (x86)\GOG Galaxy\Games\Pillars of Eternity II Deadfire\PillarsOfEternityII_Data\exported\design`
  - These are passed as CLI args, not hardcoded.

---

## Phase 1 — Generator, regenerated catalogue, coverage guard

### Task 1: Generator skeleton — parse methods + params (no lookup yet)

**Files:**
- Create: `tools/catalogue-gen/generate.py`
- Create: `tools/catalogue-gen/test_generate.py`
- Create: `tools/catalogue-gen/README.md` (how to run, input paths)

**Interfaces:**
- Produces (Python): `parse_source(text, kind) -> list[Entry]` where `kind in {"script","condition"}`; `Entry` is a dict with `methodName, fullName, displayName, category, description, parameters:[{name, type, description, default}]` (lookupKind/options added in later tasks). `fullName` built per the fidelity rules above.

- [ ] **Step 1: Write the failing test**

Create `tools/catalogue-gen/test_generate.py`:

```python
import generate

SCRIPT = r'''
[Script("Add Reputation", "Scripts\\Faction")]
[ScriptParam0("Faction", "Faction to modify.", "", "e7b2bb2a-99a8-41cb-b0f2-6f1fb973d5ac", BrowserType.GameData)]
[ScriptParam1("Axis", "Good vs. Bad action", Axis.Positive)]
[ScriptParam2("Strength", "Severity of the change", "71c858fe-7c4b-432a-a105-c518319eaed7", "32e5c672-85db-423f-b363-71c8a08674fc", BrowserType.GameData)]
public static void ReputationAddPoints(Guid factionGuid, Axis axis, Guid strengthGuid)
{
}
'''

def test_parses_method_header():
    e = generate.parse_source(SCRIPT, "script")[0]
    assert e["methodName"] == "ReputationAddPoints"
    assert e["fullName"] == "Void ReputationAddPoints(Guid, Axis, Guid)"
    assert e["displayName"] == "Add Reputation"
    assert e["category"] == "Faction"
    names = [p["name"] for p in e["parameters"]]
    assert names == ["Faction", "Axis", "Strength"]
    assert e["parameters"][0]["description"] == "Faction to modify."

def test_condition_uses_conditionalscript_attr():
    COND = r'''
    [ConditionalScript("Is Reputation", "Conditionals\\Faction")]
    [ScriptParam0("Faction", "Faction to modify.", "", "e7b2bb2a-99a8-41cb-b0f2-6f1fb973d5ac", Scripts.BrowserType.GameData)]
    [ScriptParam1("Rank Type", "type", RankType.Good)]
    [ScriptParam2("Rank", "r", 0)]
    [ScriptParam3("Operator", "op", Operator.EqualTo)]
    public static bool IsReputation(Guid factionGuid, RankType type, int rankValue, Operator comparisonOperator)
    { }
    '''
    e = generate.parse_source(COND, "condition")[0]
    assert e["fullName"] == "Boolean IsReputation(Guid, RankType, Int32, Operator)"
    assert e["displayName"] == "Is Reputation"

if __name__ == "__main__":
    import sys
    fns = [v for k, v in sorted(globals().items()) if k.startswith("test_")]
    for fn in fns:
        fn(); print("ok", fn.__name__)
    print(f"{len(fns)} passed")
```

- [ ] **Step 2: Run to verify it fails**

Run: `python tools/catalogue-gen/test_generate.py`
Expected: FAIL — `generate` module / `parse_source` missing.

- [ ] **Step 3: Implement the parser skeleton**

Create `tools/catalogue-gen/generate.py` with:
- A CLR type-spelling map: `{"void":"Void","int":"Int32","bool":"Boolean","float":"Single","string":"String","System.Guid":"Guid","Guid":"Guid"}`; any other token kept as its short name (strip namespace/`Scripts.`/nested `X.Y`→`Y` only where the game writes short form — keep as written for enums like `Axis`, `Disposition.Rank`→`Rank`).
- `parse_source(text, kind)`: scan for method blocks. For each `public static (bool|void|...) Name(params)` immediately preceded by attribute lines:
  - Read the `[Script(...)]`/`[ConditionalScript(...)]` → display + `category` (segment after `\\`).
  - Read `[ScriptParamN("Name","Desc", …)]` lines in order → param name/description (+ raw remaining args, retained for Tasks 2–3).
  - Parse the signature parameter list → CLR types; build `fullName` via the spelling map; `methodName` = Name.
  - Return entries with `parameters` = list of `{name, type(raw CLR short type), description, default:""}` (default filled in Task 2).

Keep parsing tolerant: methods without a `[Script]`/`[ConditionalScript]` attribute are skipped (not catalogue-exposed).

- [ ] **Step 4: Run to verify it passes**

Run: `python tools/catalogue-gen/test_generate.py`
Expected: PASS (both tests).

- [ ] **Step 5: Commit**

```bash
git add tools/catalogue-gen/generate.py tools/catalogue-gen/test_generate.py tools/catalogue-gen/README.md
git commit -m "feat(catalogue-gen): parse method headers + params from decompiled sources"
```

---

### Task 2: Enum options + parameter defaults + `type` field

**Files:**
- Modify: `tools/catalogue-gen/generate.py`
- Modify: `tools/catalogue-gen/test_generate.py`

**Interfaces:**
- Produces: each parameter gains `type` and `default`. Enum params → `type: "Enum:<EnumName>"` + `options:[members]`; value params → `type` = CLR spelling (`Int32`/`Boolean`/`String`/`Single`); GUID/GameData params → `type` in `{"Guid","ObjectGuid","GameData"}` (refined in Task 3). `parse_enums(text) -> dict[name, list[str]]` extracts enum member lists.

- [ ] **Step 1: Write the failing test**

Append to `test_generate.py`:

```python
ENUMS = r'''
public enum Axis { Positive, Negative }
public enum RankType { Good = 0, Bad = 1 }
'''

def test_parse_enums():
    m = generate.parse_enums(ENUMS)
    assert m["Axis"] == ["Positive", "Negative"]
    assert m["RankType"] == ["Good", "Bad"]

def test_enum_param_gets_options_and_default():
    enums = generate.parse_enums(ENUMS)
    e = generate.parse_source(SCRIPT, "script", enums=enums)[0]
    axis = e["parameters"][1]
    assert axis["type"] == "Enum:Axis"
    assert axis["options"] == ["Positive", "Negative"]
    assert axis["default"] == "Positive"

def test_value_param_type_and_default():
    enums = generate.parse_enums(ENUMS)
    e = generate.parse_source(SCRIPT.replace("Guid strengthGuid","int amount")
                                    .replace('"Strength", "Severity of the change", "71c858fe-7c4b-432a-a105-c518319eaed7", "32e5c672-85db-423f-b363-71c8a08674fc", BrowserType.GameData',
                                             '"Amount","n",5'),
                              "script", enums=enums)[0]
    amt = e["parameters"][2]
    assert amt["type"] == "Int32"
    assert amt["default"] == "5"
```

- [ ] **Step 2: Run to verify it fails**

Run: `python tools/catalogue-gen/test_generate.py`
Expected: FAIL — `parse_enums` missing; params lack `type`/`options`/`default`.

- [ ] **Step 3: Implement**

- `parse_enums(text)`: regex `enum (\w+)\s*\{([^}]*)\}`; split members on `,`, strip `= value` and whitespace; keep order.
- In `parse_source`, accept `enums` dict. For each param, use the CLR type + the `ScriptParamN` args:
  - Enum type (name present in `enums`, or a nested `X.Y` whose `Y` is in enums) → `type="Enum:<Name>"`, `options=enums[Name]`, `default` = the attribute's default rendered as the member name (`Axis.Positive`→`Positive`; a bare identifier `Positive`→`Positive`).
  - `int/bool/float/string` → `type` via spelling map; `default` = the literal (`5`, `true`→`true`, quoted string unquoted).
  - `Guid` param → provisional `type="Guid"` (Task 3 sets `ObjectGuid`/`GameData` + lookupKind).
  - No default arg → `default=""`.

- [ ] **Step 4: Run to verify it passes**

Run: `python tools/catalogue-gen/test_generate.py`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tools/catalogue-gen/generate.py tools/catalogue-gen/test_generate.py
git commit -m "feat(catalogue-gen): enum options, param types and defaults"
```

---

### Task 3: `BrowserType` → lookupKind, incl. GameData `$type` resolution

**Files:**
- Modify: `tools/catalogue-gen/generate.py`
- Modify: `tools/catalogue-gen/test_generate.py`

**Interfaces:**
- Produces: `build_guid_type_index(bundle_dir) -> dict[guid_lower, typename]`; `TYPE_TO_KIND` map (`FactionGameData→Faction`, `ChangeStrengthGameData→ChangeStrength`, `AbilityGameData→Ability`, etc.); each GUID/GameData param gains `lookupKind` and a refined `type`.
- Lookup mapping applied in `parse_source(..., guid_index=…)`:
  - `BrowserType.None`/absent → no lookupKind; `type` stays `Guid` if the CLR type is Guid.
  - `GlobalVariable→GlobalVariable`, `Conversation→Conversation`, `Quest→Quest` (type `GameData`), `ObjectGuid→Speaker` (type `ObjectGuid`), `Chatter→Speaker`.
  - `GameData` → resolve the param's default GUID(s) via `guid_index`; map `$type`→kind through `TYPE_TO_KIND`; type `GameData`. Unresolvable/empty default → `lookupKind="GameData"` (generic, dormant).
  - `GlobalScript/GlobalConditional/GlobalPreference` → no lookupKind.

- [ ] **Step 1: Write the failing test**

Append to `test_generate.py`:

```python
def test_gamedata_param_resolves_specific_kind():
    idx = {
        "e7b2bb2a-99a8-41cb-b0f2-6f1fb973d5ac": "FactionGameData",
        "71c858fe-7c4b-432a-a105-c518319eaed7": "ChangeStrengthGameData",
    }
    enums = generate.parse_enums(ENUMS)
    e = generate.parse_source(SCRIPT, "script", enums=enums, guid_index=idx)[0]
    assert e["parameters"][0]["lookupKind"] == "Faction"
    assert e["parameters"][0]["type"] == "GameData"
    assert e["parameters"][2]["lookupKind"] == "ChangeStrength"

def test_objectguid_maps_to_speaker():
    src = r'''
    [Script("X","Scripts\\Misc")]
    [ScriptParam0("Who","w","", "", BrowserType.ObjectGuid)]
    public static void X(Guid who) { }
    '''
    e = generate.parse_source(src, "script", enums={}, guid_index={})[0]
    assert e["parameters"][0]["lookupKind"] == "Speaker"
    assert e["parameters"][0]["type"] == "ObjectGuid"

def test_unresolvable_gamedata_is_generic():
    src = r'''
    [Script("X","Scripts\\Misc")]
    [ScriptParam0("D","d","", "", BrowserType.GameData)]
    public static void X(Guid d) { }
    '''
    e = generate.parse_source(src, "script", enums={}, guid_index={})[0]
    assert e["parameters"][0]["lookupKind"] == "GameData"
```

- [ ] **Step 2: Run to verify it fails**

Run: `python tools/catalogue-gen/test_generate.py`
Expected: FAIL — lookupKind not produced.

- [ ] **Step 3: Implement**

- `build_guid_type_index(bundle_dir)`: walk `*.gamedatabundle`, for each object record `ID.lower() → $type` short name (strip `Game.GameData.` and `, Assembly-CSharp`).
- `TYPE_TO_KIND`: start from the kinds the runtime already knows, mapping the confirmed `$type`s: `FactionGameData→Faction, DeityGameData→Deity, DispositionGameData→Disposition, ChangeStrengthGameData→ChangeStrength, PaladinOrderGameData→PaladinOrder, ItemGameData→Item (and *ItemGameData→Item), AbilityGameData→Ability, PhraseGameData→Phrase, StatusEffectGameData→StatusEffect, KeywordGameData→Keyword, MapGameData→Map, BaseStatsGameData→Class, RaceGameData→Race, SubraceGameData→Subrace, BackgroundGameData→Background, CultureGameData→Culture, SkillGameData→Skill, WeaponTypeGameData→WeaponType`. Any `$type` not in the map → kind = strip the trailing `GameData` (best-effort; dormant unless a loader exists).
- In `parse_source`, read each `ScriptParamN`'s trailing `BrowserType.X` token (may be `Scripts.BrowserType.X`); apply the mapping table above. For `GameData`, take the **last** quoted GUID in the attribute args as the resolving default (attributes carry 1–2 default GUIDs; the last is the concrete default value), look it up in `guid_index`.

- [ ] **Step 4: Run to verify it passes**

Run: `python tools/catalogue-gen/test_generate.py`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tools/catalogue-gen/generate.py tools/catalogue-gen/test_generate.py
git commit -m "feat(catalogue-gen): browsertype + gamedata $type -> lookupKind"
```

---

### Task 4: Merge both games + emit the JSON files

**Files:**
- Modify: `tools/catalogue-gen/generate.py` (add `main()` CLI + `merge_games`)
- Modify: `tools/catalogue-gen/test_generate.py`
- Regenerate (outputs): `DialogEditor.ViewModels/Resources/scripts.json`, `.../conditions.json`, `data/scripts.json`, `data/conditions.json`

**Interfaces:**
- Produces: `merge_games(entries_poe1, entries_poe2) -> list` keyed by `fullName`, union `games` (`["poe1"]`/`["poe2"]`/`["poe1","poe2"]`), stable order; `main()` reads CLI paths, writes all four files sorted deterministically (by category then displayName then fullName) with 2-space indent to match existing style.

- [ ] **Step 1: Write the failing test (merge logic)**

Append to `test_generate.py`:

```python
def test_merge_unions_games_for_same_signature():
    a = [{"fullName": "Void F(Guid)", "games": ["poe1"], "methodName":"F","displayName":"F","category":"C","description":"","parameters":[]}]
    b = [{"fullName": "Void F(Guid)", "games": ["poe2"], "methodName":"F","displayName":"F","category":"C","description":"","parameters":[]},
         {"fullName": "Void G()",     "games": ["poe2"], "methodName":"G","displayName":"G","category":"C","description":"","parameters":[]}]
    merged = generate.merge_games(a, b)
    byfn = {e["fullName"]: e for e in merged}
    assert byfn["Void F(Guid)"]["games"] == ["poe1", "poe2"]
    assert byfn["Void G()"]["games"] == ["poe2"]
```

- [ ] **Step 2: Run to verify it fails**

Run: `python tools/catalogue-gen/test_generate.py`
Expected: FAIL — `merge_games` missing.

- [ ] **Step 3: Implement merge + `main()`**

- `merge_games`: dict keyed by `fullName`; first occurrence wins for content; `games` = sorted union. Deterministic output order.
- `main(argv)`: args `--poe1-scripts --poe1-conditions --poe2-scripts --poe2-conditions --bundles --repo`. Parse each source with its game tag; `parse_enums` over the source text (and any referenced enum files — for v1, parse enums from the same Scripts.cs/Conditionals.cs plus a `--enums` glob if needed); build the guid index once from `--bundles`; merge; write `scripts.json` and `conditions.json` to both `DialogEditor.ViewModels/Resources/` and `data/` under `--repo`. Emit with `json.dump(..., indent=2, ensure_ascii=False)` and drop empty `options`/`values`/`lookupKind` keys.

- [ ] **Step 4: Run tests, then run the generator for real**

Run tests: `python tools/catalogue-gen/test_generate.py` → PASS.
Run the generator with the Global-Constraints paths. Then sanity-check:
```bash
python -c "import json;print('scripts',len(json.load(open('DialogEditor.ViewModels/Resources/scripts.json'))),'conditions',len(json.load(open('DialogEditor.ViewModels/Resources/conditions.json'))))"
git --no-pager diff --stat -- '*conditions.json' '*scripts.json'
```
Expected: counts jump into the hundreds; embedded and `data/` copies identical (`git diff --no-index DialogEditor.ViewModels/Resources/scripts.json data/scripts.json` → no output; same for conditions).

- [ ] **Step 5: Commit**

```bash
git add tools/catalogue-gen/generate.py tools/catalogue-gen/test_generate.py \
        DialogEditor.ViewModels/Resources/scripts.json DialogEditor.ViewModels/Resources/conditions.json \
        data/scripts.json data/conditions.json
git commit -m "feat(catalogue-gen): merge games, emit regenerated catalogue"
```

---

### Task 5: Coverage fixture + `CatalogueCoverageTests` (C#)

**Files:**
- Modify: `tools/catalogue-gen/generate.py` (emit the usage fixture)
- Create: `DialogEditor.Tests/Fixtures/catalogue-usage.txt`
- Create: `DialogEditor.Tests/Services/CatalogueCoverageTests.cs`
- Modify: `DialogEditor.Tests/DialogEditor.Tests.csproj` if fixtures need `CopyToOutputDirectory` (check how existing fixtures are referenced first).

**Interfaces:**
- Consumes: `ConditionCatalogue.Instance`, `ScriptCatalogue.Instance` (their `All` + `ReflectionFullName`).
- Produces: `catalogue-usage.txt` — one `fullName` per line (distinct script/condition signatures used across all shipped conversations), committed; `CatalogueCoverageTests` asserts each is present in the catalogue.

- [ ] **Step 1: Emit the fixture from the generator**

Add a generator mode/flag `--emit-usage <conversations_dir> <out_path>` that walks every `*.conversationbundle`, collects each `Data.FullName` (dedup, sorted), and writes them one per line to `DialogEditor.Tests/Fixtures/catalogue-usage.txt`. Run it against the conversations dir to produce the committed fixture.

- [ ] **Step 2: Write the failing test**

Create `DialogEditor.Tests/Services/CatalogueCoverageTests.cs`:

```csharp
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class CatalogueCoverageTests
{
    private static HashSet<string> CatalogueFullNames()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in ConditionCatalogue.Instance.All) set.Add(e.ReflectionFullName);
        foreach (var e in ScriptCatalogue.Instance.All)    set.Add(e.ReflectionFullName);
        return set;
    }

    [Fact]
    public void EveryShippedSignature_IsInCatalogue()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "catalogue-usage.txt");
        var used    = File.ReadAllLines(fixture)
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var known   = CatalogueFullNames();

        var missing = used.Where(fn => !known.Contains(fn)).OrderBy(x => x).ToList();
        Assert.True(missing.Count == 0,
            $"{missing.Count} shipped signatures missing from the catalogue:\n" +
            string.Join("\n", missing.Take(30)));
    }
}
```

Confirm `ScriptCatalogue` exposes `All` and entries expose `ReflectionFullName` (mirror `ConditionEntry`); if the script entry's property differs, use the actual name.

- [ ] **Step 3: Run to verify it fails, then passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~CatalogueCoverageTests"`
Expected: If Task 4 fully covered usage → PASS. If a residue remains (e.g. a handful of signatures the generator skipped), the failure lists them — fix the generator (Tasks 1–3), regenerate (Task 4), re-run until green. **Do not weaken the test.**

- [ ] **Step 4: Commit**

```bash
git add tools/catalogue-gen/generate.py DialogEditor.Tests/Fixtures/catalogue-usage.txt \
        DialogEditor.Tests/Services/CatalogueCoverageTests.cs DialogEditor.Tests/DialogEditor.Tests.csproj
git commit -m "test(catalogue): shipped-signature coverage guard + usage fixture"
```

---

### Task 6: Reconcile existing C# tests with the regenerated catalogue

**Files:**
- Modify: `DialogEditor.Tests/Services/LookupKindWhitelistTests.cs` (expand whitelist to emitted kinds)
- Modify: any catalogue-content tests that asserted old specifics (triage from the run)
- Modify: `DialogEditor.Tests/Services/ReputationFactionLookupTests.cs` if the regenerated entries changed method/param positions (should still hold: faction param is index 0, kind "Faction").

**Interfaces:** none new — this task makes the full suite green on the regenerated data.

- [ ] **Step 1: Run the full suite and triage**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`
Expected: failures concentrated in (a) `LookupKindWhitelistTests` (new kinds like `ChangeStrength`/`GameData` not in the hardcoded whitelist), and (b) any test asserting a specific catalogue entry's shape that regeneration changed. List them.

- [ ] **Step 2: Update the whitelist**

In `LookupKindWhitelistTests.cs`, add every kind the generator now emits (e.g. `ChangeStrength`, and the generic `GameData`) to `KnownKinds`. Keep the test's intent: all emitted kinds are recognised (no typos).

- [ ] **Step 3: Fix other broken content tests**

For each remaining failure, verify the regenerated entry is *correct* against the decompiled source, then update the test's expectation to match (these tests were asserting the old hand-authored specifics). Do not change production code — the schema is unchanged.

- [ ] **Step 4: Run to verify green**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`
Expected: PASS (whole suite).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Tests
git commit -m "test(catalogue): reconcile tests with regenerated catalogue"
```

---

## Phase 2 — Runtime loaders for newly-surfaced kinds

### Task 7: Register `ChangeStrength` (and any other emitted kind with a bundle source)

**Files:**
- Modify: `DialogEditor.Core/GameData/Poe2GameDataProvider.cs`
- Test: `DialogEditor.Tests/GameData/Poe2GameDataProviderTests.cs` (add cases; match the existing test file's location/style)

**Interfaces:**
- Produces: `GameDataNameService` kind `ChangeStrength` populated from `ChangeStrengthGameData` in `factions.gamedatabundle`.

- [ ] **Step 1: Identify the gap set**

Compute the kinds the regenerated catalogue references but the provider does not register. From analysis this is chiefly `ChangeStrength` (the existing loader registers the same `ChangeStrengthGameData` under the legacy kind name `DispositionStrength`). List any others surfaced by the generator's `$type→kind` output that have a bundle source but no loader.

- [ ] **Step 2: Write the failing test**

Add to `Poe2GameDataProviderTests.cs` (reuse its game-folder fixture/skip pattern — mirror an existing test such as the Faction/Disposition one):

```csharp
[SkippableFact]
public void LoadGameDataNames_RegistersChangeStrength()
{
    var provider = /* construct as sibling tests do, pointing at the game folder */;
    var names = provider.LoadGameDataNames();
    Skip.IfNot(names.ContainsKey("ChangeStrength"), "PoE2 game data not available");
    Assert.Contains(names["ChangeStrength"], e => e.Name == "Major");
}
```

Match the existing provider tests' construction and skip mechanism exactly (they already handle "game folder not present").

- [ ] **Step 3: Implement**

In `Poe2GameDataProvider.LoadGameDataNames()`, register `ChangeStrength` from `ChangeStrengthGameData`. Since the same `$type` currently backs `DispositionStrength`, change that registration to the generator's canonical kind name `ChangeStrength` (the generator emits `ChangeStrength` for every `ChangeStrengthGameData` param, including disposition-strength ones), keeping the DebugName as-is:

```csharp
// ChangeStrengthGameData is the shared strength object (Major/Minor/Average…) used by
// reputation, disposition, and relationship change scripts. Canonical kind: ChangeStrength.
FactionBundle("ChangeStrength", "ChangeStrengthGameData");
```

Remove the old `FactionBundle("DispositionStrength", "ChangeStrengthGameData")` line (the catalogue no longer emits `DispositionStrength`; confirm via a grep that no regenerated entry uses it — if any do, keep both registrations pointing at the same `$type`).

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~Poe2GameDataProviderTests"`
Expected: PASS (skips if game folder absent — then verify in Task 8's app run instead).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/GameData/Poe2GameDataProvider.cs DialogEditor.Tests/GameData/Poe2GameDataProviderTests.cs
git commit -m "feat(catalogue): register ChangeStrength lookup (reputation/disposition strength)"
```

---

### Task 8: App verification + tracking updates

**Files:**
- Modify: `BUGS.md` (log the systemic fix), `Gaps.md` (note catalogue now regenerated/complete)

- [ ] **Step 1: Verify node 177 in the running app**

Use the `running-the-app` skill (back up settings; point at a scratch project; a PoE2 game folder is configured). Open `08_cv_atsura`, select node 177, inspect its `Add Reputation` (`ReputationAddPoints`) On-Enter script:
- Param 1 (Faction) shows a faction **name** (Royal Deadfire Company) with a working autocomplete, not a raw GUID.
- Param 3 (Strength) shows **Major** (ChangeStrength), not a raw GUID.
Screenshot for the record.

- [ ] **Step 2: Spot-check breadth**

Open 2–3 other previously-raw conversations/scripts (e.g. a `TriggerTopic`, an `IsDisposition`) and confirm their GUID params now resolve to names.

- [ ] **Step 3: Full suite + generator tests**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj` and `python tools/catalogue-gen/test_generate.py`
Expected: both green.

- [ ] **Step 4: Update BUGS.md / Gaps.md**

- `BUGS.md`: add a Fixed entry (`B-011`) for the systemic catalogue incompleteness (node 177 repro), referencing the coverage guard and the fixing commit hash.
- `Gaps.md`: under "Parameter Readability — Beyond Characters", note the catalogue is now generated from decompiled sources and coverage is enforced by `CatalogueCoverageTests`; list any kinds still dormant (no bundle source).

- [ ] **Step 5: Commit**

```bash
git add BUGS.md Gaps.md
git commit -m "docs: record catalogue regeneration (B-011) + gap update"
```

---

## Self-Review

**Spec coverage:**
- Generator (parse, enums, browsertype/GameData resolution, merge, emit) → Tasks 1–4. ✔
- lookup-kind inference incl. `GameData` `$type` resolution + generic fallback → Task 3. ✔
- Both-games merge, schema preserved, dual-file emit, `fullName` fidelity → Tasks 1 & 4 + Global Constraints. ✔
- Coverage fixture + regression test → Task 5. ✔
- Whitelist/content test reconciliation → Task 6. ✔
- Runtime loader for `ChangeStrength`/new kinds → Task 7. ✔
- App verification + tracking → Task 8. ✔
- Phasing (Phase 1 fixes majority; Phase 2 stragglers) → task grouping. ✔

**Placeholder scan:** The generator steps describe concrete parsing rules with worked test cases; remaining latitude (regex specifics) is inherent to parsing real decompiled text and is gated by concrete acceptance tests (`test_generate.py` + `CatalogueCoverageTests`). Instructions to "match the existing test/loader style" name the exact files to copy from — reuse directives, not gaps.

**Type/name consistency:** `parse_source`/`parse_enums`/`merge_games`/`build_guid_type_index` signatures are consistent across the tasks that define and call them. JSON keys match the frozen schema. `ReflectionFullName` used for matching in Task 5 matches `ConditionEntry.ReflectionFullName`; confirm the script entry's equivalent during Task 5. Kind name `ChangeStrength` is consistent between generator output (Task 3), whitelist (Task 6), and loader (Task 7).

**Risk called out:** full regen may break catalogue-content tests (Task 6 handles triage); `fullName` spelling must match runtime data exactly (guarded by Task 5). The generator's Python tests aren't run in CI — the C# `CatalogueCoverageTests` is the authoritative net.
