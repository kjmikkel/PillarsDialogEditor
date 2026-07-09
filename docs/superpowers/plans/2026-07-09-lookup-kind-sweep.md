# Generic Bundle Sweep for Lookup Kinds Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Register every bundle-backed lookup kind the regenerated catalogue references (~30 currently dormant: Ship, Team, CharacterClass, Affliction, Schedule, CreatureType, …) so their GUID parameters resolve to names instead of raw GUIDs.

**Architecture:** A one-pass sweep in `Poe2GameDataProvider.LoadGameDataNames()` parses every `*.gamedatabundle` once (`Poe2GameDataBundleParser.ParseAllByType`), maps each short `$type` to a kind via a tiny `GameDataKindMapper` (C# mirror of the generator's three-rule mapping), and registers all buckets. The four explicit registrations with real cleaning/filtering (Disposition, PaladinOrder, Class, WeaponType) run after the sweep and overwrite it; the 14 plain per-kind registrations the sweep reproduces are deleted. Quest/GlobalVariable/Conversation/Speaker paths are untouched.

**Tech Stack:** C# / .NET 8, System.Text.Json, xUnit. No new dependencies.

## Global Constraints

- **TDD, red first.** Tests in `DialogEditor.Tests` mirroring source structure; suite runs serially (do not re-enable parallelism).
- **`TypeToKind` contract (exactly three rules, mirroring `tools/catalogue-gen/generate.py` `type_to_kind`):** (1) `BaseStatsGameData` → `Class`; (2) any type ending `ItemGameData` → `Item`; (3) otherwise strip trailing `GameData`, then alias `GenericAbility` → `Ability`. Cross-referencing comments on both sides.
- **Sweep skip rules:** objects with empty/whitespace `ID` or `DebugName` are skipped (same as `Parse` today); missing/unparseable file → empty result.
- **Explicit-wins order:** sweep populates the result dict first; explicit registrations assign afterwards, overwriting the same kind key.
- **Error handling:** no bare `catch`; any caught exception logged via `AppLog.Error`/`Warn` (`OperationCanceledException` excepted). The parser follows `ParseFile`'s existing missing-file behaviour (return empty, no throw).
- **Behaviour change (intended):** Item/Ability/StatusEffect narrow to their true `$type`s (today they include LootLists/Phrases/Afflictions from shared files). Reconcile, don't revert.
- **Build/test:** `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`; filter single classes with `--filter "FullyQualifiedName~<name>"`.

---

### Task 1: `GameDataKindMapper`

**Files:**
- Create: `DialogEditor.Core/GameData/GameDataKindMapper.cs`
- Test: `DialogEditor.Tests/GameData/GameDataKindMapperTests.cs` (create)

**Interfaces:**
- Produces: `public static class GameDataKindMapper { public static string TypeToKind(string shortType); }` — input is a short `$type` name (e.g. `"ShipGameData"`), output the `GameDataNameService` kind (e.g. `"Ship"`).

- [ ] **Step 1: Write the failing test**

Create `DialogEditor.Tests/GameData/GameDataKindMapperTests.cs`:

```csharp
using DialogEditor.Core.GameData;

namespace DialogEditor.Tests.GameData;

public class GameDataKindMapperTests
{
    [Theory]
    [InlineData("ShipGameData", "Ship")]                    // plain suffix strip
    [InlineData("ChangeStrengthGameData", "ChangeStrength")]
    [InlineData("BaseStatsGameData", "Class")]              // rule 1: explicit override
    [InlineData("ConsumableItemGameData", "Item")]          // rule 2: *ItemGameData -> Item
    [InlineData("ItemGameData", "Item")]
    [InlineData("GenericAbilityGameData", "Ability")]       // rule 3 alias
    [InlineData("NotAGameDataType", "NotAGameDataType")]    // no suffix: pass through
    public void TypeToKind_MapsPerContract(string shortType, string expected)
        => Assert.Equal(expected, GameDataKindMapper.TypeToKind(shortType));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~GameDataKindMapperTests"`
Expected: FAIL — `GameDataKindMapper` does not exist (compile error).

- [ ] **Step 3: Implement**

Create `DialogEditor.Core/GameData/GameDataKindMapper.cs`:

```csharp
namespace DialogEditor.Core.GameData;

/// Maps a GameData object's short $type name to its GameDataNameService lookup kind.
/// MUST mirror tools/catalogue-gen/generate.py `type_to_kind` (the generator stamps
/// these kind names into the catalogue's lookupKind fields offline; this side resolves
/// them at runtime). Exactly three rules — change both sides together.
public static class GameDataKindMapper
{
    public static string TypeToKind(string shortType)
    {
        if (shortType == "BaseStatsGameData") return "Class";
        if (shortType.EndsWith("ItemGameData", StringComparison.Ordinal)) return "Item";
        var kind = shortType.EndsWith("GameData", StringComparison.Ordinal)
            ? shortType[..^"GameData".Length]
            : shortType;
        return kind == "GenericAbility" ? "Ability" : kind;
    }
}
```

Also add the cross-reference on the Python side — in `tools/catalogue-gen/generate.py`, extend the comment above `_TYPE_KIND_OVERRIDE`:

```python
# GameData $type -> lookupKind. Any *ItemGameData -> Item; otherwise strip the
# trailing "GameData". A few explicit overrides keep names aligned with the
# runtime loaders (GameDataNameService kinds).
# MUST mirror DialogEditor.Core/GameData/GameDataKindMapper.cs (the runtime
# sweep resolves the kind names this generator stamps into the catalogue).
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~GameDataKindMapperTests"`
Expected: PASS (all 7 cases).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/GameData/GameDataKindMapper.cs DialogEditor.Tests/GameData/GameDataKindMapperTests.cs tools/catalogue-gen/generate.py
git commit -m "feat(lookups): GameDataKindMapper mirroring the generator's type->kind rules"
```

---

### Task 2: `Poe2GameDataBundleParser.ParseAllByType`

**Files:**
- Modify: `DialogEditor.Core/Parsing/Poe2GameDataBundleParser.cs`
- Test: `DialogEditor.Tests/Parsing/Poe2GameDataBundleParserTests.cs` (add cases; if the file does not exist, create it with this class)

**Interfaces:**
- Consumes: the parser's existing private `BundleRoot`/`BundleObject` records and `Options`.
- Produces:
  - `public static IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>> ParseAllByType(string json)`
  - `public static IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>> ParseAllByTypeFile(string path)`
  Keys are **short** `$type` names (`"Game.GameData.ShipGameData, Assembly-CSharp"` → `"ShipGameData"`). `GameDataEntry(Id, Name)` with `Name = DebugName` (no cleaning).

- [ ] **Step 1: Write the failing test**

Check whether `DialogEditor.Tests/Parsing/Poe2GameDataBundleParserTests.cs` exists; add to it (or create the class) these cases:

```csharp
using DialogEditor.Core.Parsing;

namespace DialogEditor.Tests.Parsing;

public class Poe2GameDataBundleParserSweepTests
{
    private const string MixedBundle = """
        {"GameDataObjects":[
          {"$type":"Game.GameData.ShipGameData, Assembly-CSharp",
           "DebugName":"SHP_Defiant","ID":"11111111-1111-1111-1111-111111111111"},
          {"$type":"Game.GameData.ShipGameData, Assembly-CSharp",
           "DebugName":"SHP_Dhow","ID":"22222222-2222-2222-2222-222222222222"},
          {"$type":"Game.GameData.ShipUpgradeGameData, Assembly-CSharp",
           "DebugName":"SHP_UP_Sails","ID":"33333333-3333-3333-3333-333333333333"},
          {"$type":"Game.GameData.ShipGameData, Assembly-CSharp",
           "DebugName":"","ID":"44444444-4444-4444-4444-444444444444"},
          {"$type":"Game.GameData.ShipGameData, Assembly-CSharp",
           "DebugName":"NoId","ID":""}
        ]}
        """;

    [Fact]
    public void ParseAllByType_GroupsByShortType()
    {
        var byType = Poe2GameDataBundleParser.ParseAllByType(MixedBundle);
        Assert.Equal(2, byType["ShipGameData"].Count);
        Assert.Single(byType["ShipUpgradeGameData"]);
        Assert.Equal("SHP_Defiant", byType["ShipGameData"][0].Name);
    }

    [Fact]
    public void ParseAllByType_SkipsEmptyIdOrDebugName()
    {
        var byType = Poe2GameDataBundleParser.ParseAllByType(MixedBundle);
        Assert.DoesNotContain(byType["ShipGameData"], e => e.Name == "NoId");
        Assert.DoesNotContain(byType["ShipGameData"], e => e.Name.Length == 0);
    }

    [Fact]
    public void ParseAllByTypeFile_MissingFile_ReturnsEmpty()
        => Assert.Empty(Poe2GameDataBundleParser.ParseAllByTypeFile(
            Path.Combine(Path.GetTempPath(), "does-not-exist.gamedatabundle")));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~Poe2GameDataBundleParserSweepTests"`
Expected: FAIL — `ParseAllByType` does not exist.

- [ ] **Step 3: Implement**

Add to `Poe2GameDataBundleParser` (below `ParseFile`):

```csharp
    /// One-pass sweep: every valid object in the bundle grouped by its SHORT $type
    /// name ("Game.GameData.ShipGameData, Assembly-CSharp" -> "ShipGameData").
    /// Used by Poe2GameDataProvider's generic lookup-kind sweep — see
    /// docs/superpowers/specs/2026-07-09-lookup-kind-sweep-design.md.
    public static IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>> ParseAllByType(string json)
    {
        var root = JsonSerializer.Deserialize<BundleRoot>(json, Options);
        if (root is null) return new Dictionary<string, IReadOnlyList<GameDataEntry>>();

        var byType = new Dictionary<string, List<GameDataEntry>>(StringComparer.Ordinal);
        foreach (var o in root.GameDataObjects)
        {
            if (string.IsNullOrWhiteSpace(o.Id) || string.IsNullOrWhiteSpace(o.DebugName))
                continue;
            var shortType = ShortTypeName(o.DataType);
            if (shortType.Length == 0) continue;
            if (!byType.TryGetValue(shortType, out var list))
                byType[shortType] = list = [];
            list.Add(new GameDataEntry(Id: o.Id, Name: o.DebugName));
        }
        return byType.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<GameDataEntry>)kv.Value);
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>> ParseAllByTypeFile(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, IReadOnlyList<GameDataEntry>>();
        var text = File.ReadAllText(path, new System.Text.UTF8Encoding(true));
        return ParseAllByType(text);
    }

    /// "Game.GameData.ShipGameData, Assembly-CSharp" -> "ShipGameData".
    private static string ShortTypeName(string? dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType)) return string.Empty;
        var beforeComma = dataType.Split(',')[0].Trim();
        var lastDot = beforeComma.LastIndexOf('.');
        return lastDot >= 0 ? beforeComma[(lastDot + 1)..] : beforeComma;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~Poe2GameDataBundleParserSweepTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/Parsing/Poe2GameDataBundleParser.cs DialogEditor.Tests/Parsing/Poe2GameDataBundleParserTests.cs
git commit -m "feat(lookups): one-pass ParseAllByType bundle sweep"
```

(If the test file was newly created under a different name — e.g. `Poe2GameDataBundleParserSweepTests.cs` — commit that path instead.)

---

### Task 3: Provider sweep integration (explicit-wins) + reconcile

**Files:**
- Modify: `DialogEditor.Core/GameData/Poe2GameDataProvider.cs:85-184` (`LoadGameDataNames`)
- Test: `DialogEditor.Tests/GameData/Poe2GameDataProviderTests.cs` (add cases)
- Modify: any existing tests broken by the intended Item/Ability/StatusEffect narrowing (triage; reconcile, don't revert).

**Interfaces:**
- Consumes: `ParseAllByTypeFile` (Task 2), `GameDataKindMapper.TypeToKind` (Task 1).
- Produces: `LoadGameDataNames()` result now contains every bundle-backed kind (Ship, Team, CharacterClass, Affliction, Schedule, CreatureType, …), with Disposition/PaladinOrder/Class/WeaponType still explicitly cleaned/filtered.

- [ ] **Step 1: Write the failing tests**

Add to `Poe2GameDataProviderTests.cs` (reuse its temp-root fixture; the helper writes into `PillarsOfEternityII_Data/exported/design/gamedata`):

```csharp
    private string GameDataDir()
    {
        var dir = Path.Combine(_root, "PillarsOfEternityII_Data", "exported", "design", "gamedata");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void LoadGameDataNames_Sweep_RegistersShipFromShipsBundle()
    {
        File.WriteAllText(Path.Combine(GameDataDir(), "ships.gamedatabundle"), """
            {"GameDataObjects":[
              {"$type":"Game.GameData.ShipGameData, Assembly-CSharp",
               "DebugName":"SHP_Defiant","ID":"11111111-1111-1111-1111-111111111111"}
            ]}
            """);
        var names = _provider.LoadGameDataNames();
        Assert.Contains(names["Ship"], e => e.Name == "SHP_Defiant");
    }

    [Fact]
    public void LoadGameDataNames_Sweep_RegistersScheduleFromAiBundle()
    {
        File.WriteAllText(Path.Combine(GameDataDir(), "ai.gamedatabundle"), """
            {"GameDataObjects":[
              {"$type":"Game.GameData.ScheduleGameData, Assembly-CSharp",
               "DebugName":"Schedule Townie","ID":"22222222-2222-2222-2222-222222222222"}
            ]}
            """);
        var names = _provider.LoadGameDataNames();
        Assert.Contains(names["Schedule"], e => e.Name == "Schedule Townie");
    }

    [Fact]
    public void LoadGameDataNames_ExplicitDispositionCleaning_WinsOverSweep()
    {
        File.WriteAllText(Path.Combine(GameDataDir(), "factions.gamedatabundle"), """
            {"GameDataObjects":[
              {"$type":"Game.GameData.DispositionGameData, Assembly-CSharp",
               "DebugName":"HonestDisposition","ID":"33333333-3333-3333-3333-333333333333"}
            ]}
            """);
        var names = _provider.LoadGameDataNames();
        // Explicit registration strips the "Disposition" suffix; the raw sweep would not.
        Assert.Contains(names["Disposition"], e => e.Name == "Honest");
        Assert.DoesNotContain(names["Disposition"], e => e.Name == "HonestDisposition");
    }

    [Fact]
    public void LoadGameDataNames_ItemKind_ExcludesLootLists()
    {
        File.WriteAllText(Path.Combine(GameDataDir(), "items.gamedatabundle"), """
            {"GameDataObjects":[
              {"$type":"Game.GameData.ConsumableItemGameData, Assembly-CSharp",
               "DebugName":"Potion","ID":"44444444-4444-4444-4444-444444444444"},
              {"$type":"Game.GameData.LootListGameData, Assembly-CSharp",
               "DebugName":"LL_Quest","ID":"55555555-5555-5555-5555-555555555555"}
            ]}
            """);
        var names = _provider.LoadGameDataNames();
        Assert.Contains(names["Item"], e => e.Name == "Potion");
        Assert.DoesNotContain(names["Item"], e => e.Name == "LL_Quest");
        Assert.Contains(names["LootList"], e => e.Name == "LL_Quest");
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~Poe2GameDataProviderTests"`
Expected: the 4 new tests FAIL (no "Ship"/"Schedule"/"LootList" keys; Item includes the loot list); existing tests still pass.

- [ ] **Step 3: Restructure `LoadGameDataNames`**

Replace the body's registration section: sweep first, then explicit overrides. The result method:

```csharp
    public IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>> LoadGameDataNames()
    {
        var result = new Dictionary<string, IReadOnlyList<GameDataEntry>>();
        var gdRoot = GameDataRoot;

        // ── Phase 1 — generic sweep ──────────────────────────────────────────
        // Parse every bundle once, bucket objects by short $type, and register each
        // bucket under GameDataKindMapper.TypeToKind. This lights up every
        // bundle-backed lookup kind the generated catalogue references (Ship, Team,
        // Affliction, Schedule, …) and any future kind, with zero per-kind code.
        // Spec: docs/superpowers/specs/2026-07-09-lookup-kind-sweep-design.md.
        if (Directory.Exists(gdRoot))
        {
            var byKind = new Dictionary<string, List<GameDataEntry>>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(gdRoot, "*.gamedatabundle"))
            {
                foreach (var (shortType, entries) in Poe2GameDataBundleParser.ParseAllByTypeFile(file))
                {
                    var kind = GameDataKindMapper.TypeToKind(shortType);
                    if (!byKind.TryGetValue(kind, out var list))
                        byKind[kind] = list = [];
                    list.AddRange(entries);
                }
            }
            foreach (var (kind, entries) in byKind)
                result[kind] = entries;
        }

        // ── Phase 2 — explicit overrides (overwrite the sweep) ──────────────
        // Only kinds needing cleaning/filtering the sweep can't express.
        void FactionBundle(string kind, string typeFilter, Func<string, string>? clean = null)
        {
            var entries = Poe2GameDataBundleParser.ParseFile(
                Path.Combine(gdRoot, "factions.gamedatabundle"), clean, typeFilter);
            if (entries.Count > 0) result[kind] = entries;
        }

        // Disposition DebugName format: "<Name>Disposition" — strip the suffix.
        FactionBundle("Disposition", "DispositionGameData",
                      n => n.EndsWith("Disposition", StringComparison.Ordinal)
                           ? n[..^"Disposition".Length].TrimEnd() : n);
        // PaladinOrder DebugName format: "Bleak_Walkers" — replace underscores.
        FactionBundle("PaladinOrder", "PaladinOrderGameData",
                      n => n.Replace('_', ' '));

        // Class: BaseStatsGameData includes NPC creature archetypes — keep only
        // playable classes (IsPlayerClass:"true"); the sweep's bucket is unfiltered.
        var classEntries = Poe2GameDataBundleParser.ParseFile(
            Path.Combine(gdRoot, "characters.gamedatabundle"),
            typeFilter: "BaseStatsGameData", componentFilter: IsPlayerClassComponent);
        if (classEntries.Count > 0) result["Class"] = classEntries;

        // WeaponType conditions store DebugName (e.g. "Unarmed"), not the GUID → strip Id.
        var weaponEntries = Poe2GameDataBundleParser
            .ParseFile(Path.Combine(gdRoot, "global.gamedatabundle"), typeFilter: "WeaponTypeGameData")
            .Select(e => new GameDataEntry(Id: string.Empty, Name: e.Name))
            .ToList();
        if (weaponEntries.Count > 0) result["WeaponType"] = weaponEntries;

        // ── Quest / GlobalVariables / Conversations — unchanged below ───────
        …existing Quest, GlobalVariables.csv, and Conversations blocks verbatim…
```

Concretely: delete the old `Bundle`/`CharBundle` local functions and the 14 plain registrations (`Item`, `Ability`, `StatusEffect`, `Phrase`, `Keyword`, `Map`, `Faction`, `Deity`, `ChangeStrength`, `Race`, `Subrace`, `Background`, `Culture`, `Skill`); keep the Quest/GlobalVariable/Conversation blocks exactly as they are; replace the trailing "Deferred — no data source located" comment with:

```csharp
        // Kinds with no bundle-backed $type stay dormant (empty suggestions, raw GUID
        // display — safe): ProgressionUnlockable, AttackBase, and the generic "GameData"
        // fallback kind. ArmorTypeGameData is also absent from shipped bundles; if a
        // patch ever adds it, the sweep registers it automatically.
```

- [ ] **Step 4: Run the provider tests, then the full suite; triage**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~Poe2GameDataProviderTests"` → all (13 existing + 4 new) PASS.
Then: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj` → if any test asserted the old inclusive Item/Ability/StatusEffect contents or the deleted local functions' behaviour, verify the new behaviour is correct per the spec and update the test's expectation. Do not restore the plain registrations.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/GameData/Poe2GameDataProvider.cs DialogEditor.Tests
git commit -m "feat(lookups): generic bundle sweep registers all bundle-backed lookup kinds"
```

---

### Task 4: App verification + docs

**Files:**
- Modify: `Gaps.md` (dormant-kinds note + stale CreatureType claim)

- [ ] **Step 1: Data-level check against the real game bundles**

With the real install (`D:\Program Files (x86)\GOG Galaxy\Games\Pillars of Eternity II Deadfire\PillarsOfEternityII_Data\exported\design\gamedata`), run a scratch console check or a temporary `[Fact]` (delete after) constructing `Poe2GameDataProvider` over the install root and asserting `LoadGameDataNames()` contains `Ship`, `Team`, `Affliction`, `Schedule`, `CreatureType`, and that `Disposition` names carry no `Disposition` suffix. Alternatively verify via the app run in Step 2 plus the fixture tests. Record the observed kind count.

- [ ] **Step 2: Run the app**

Use the `running-the-app` skill (backup settings → scratch project → restore). Launch; confirm clean startup with a PoE2 game folder configured (the sweep runs at folder open — watch for a startup slowdown or errors in the log). If feasible, open a conversation with a `SetTeamRelationship` or ship script and confirm the GUID param shows a name; otherwise the fixture tests + data-level check stand as verification (canvas-to-node driving is unreliable via UIA).

- [ ] **Step 3: Update `Gaps.md`**

In the blockquote under "Parameter Readability — Beyond Characters" (added 2026-07-07), replace the sentence about dormant kinds ("Many `$type`-derived kinds have no runtime `GameDataNameService` loader yet and stay **dormant** … — a bounded follow-up.") with:

```markdown
> Runtime coverage completed (2026-07-09): a **generic bundle sweep** in
> `Poe2GameDataProvider` parses every `*.gamedatabundle` once and registers every
> `$type` bucket under `GameDataKindMapper.TypeToKind` (the C# mirror of the
> generator's mapping), so all ~30 formerly-dormant kinds (Ship, Team, Affliction,
> Schedule, CreatureType, …) resolve to names; explicit cleaned registrations
> (Disposition, PaladinOrder, Class, WeaponType) overwrite the sweep. Only
> `ProgressionUnlockable`, `AttackBase`, and the generic `GameData` kind remain
> dormant (no bundle source). Spec:
> `docs/superpowers/specs/2026-07-09-lookup-kind-sweep-design.md`.
```

In the "Still deferred" list further down, correct the `CreatureType` bullet: it stated `CreatureTypeGameData` was absent from all bundles, but it exists (110 objects in `characters.gamedatabundle`) and is now registered by the sweep; `ArmorType` remains genuinely absent.

- [ ] **Step 4: Full suite + commit**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj` → PASS.

```bash
git add Gaps.md
git commit -m "docs(gaps): lookup kinds resolved by generic sweep; fix stale CreatureType claim"
```

---

## Self-Review

**Spec coverage:** §1 ParseAllByType → Task 2; §2 GameDataKindMapper + cross-refs → Task 1; §3 sweep + explicit-wins + deletions + untouched paths + stale-comment replacement → Task 3; testing section's five bullets → Tasks 1–3 tests + Task 3 step 4 regression; docs → Task 4. ✔

**Placeholder scan:** the one elision ("…existing Quest, GlobalVariables.csv, and Conversations blocks verbatim…") is an explicit keep-as-is directive over code shown earlier in the same task's context (provider lines 152–178), not deferred work. ✔

**Type consistency:** `ParseAllByType(string json)` / `ParseAllByTypeFile(string path)` names match between Task 2 (definition) and Task 3 (consumption); `GameDataKindMapper.TypeToKind` matches Tasks 1/3; `GameDataEntry(Id, Name)` is the existing Core record; fixture paths match `Poe2GameDataProviderTests`' `_root` layout (verified against the real test file). ✔

**Known risk:** existing tests asserting inclusive Item/Ability contents (Task 3 step 4 triages); sweep memory (~25k entries of two strings) and one-pass parse cost are both smaller than today's repeated per-kind parses.
