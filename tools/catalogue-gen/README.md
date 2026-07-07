# Catalogue generator

Regenerates `scripts.json` / `conditions.json` (the script/condition catalogue) from the
decompiled game sources plus the exported game bundles. Reproducible replacement for the
former hand-authored catalogue.

Design: `docs/superpowers/specs/2026-07-07-catalogue-regeneration-design.md`
Plan:   `docs/superpowers/plans/2026-07-07-catalogue-regeneration.md`

## What it does

- Parses the `[Script]/[ConditionalScript]` + `[ScriptParamN]` attribute grammar in the
  decompiled `Scripts.cs` / `Conditionals.cs` (PoE1 + PoE2).
- Resolves enum option lists (from enum definitions across the decompiled source tree).
- Resolves each `BrowserType.GameData` parameter's concrete lookup kind by looking up its
  default GUID in the exported `*.gamedatabundle` files (`GUID -> $type -> kind`).
- Merges the two games by signature (union the `games` field) and writes:
  - `DialogEditor.ViewModels/Resources/scripts.json` + `.../conditions.json` (embedded)
  - `data/scripts.json` + `data/conditions.json` (human-readable mirror)
  - `DialogEditor.Tests/Fixtures/catalogue-usage.txt` (coverage fixture)

The generated JSON keeps the exact existing schema, so no C# consumer changes.

## Run (author machine)

```
python tools/catalogue-gen/generate.py \
  --poe1-scripts     "C:/Users/kjmik/Documents/Programming/Deadfire/PoE1 Code/Assembly-CSharp/Scripts.cs" \
  --poe1-conditions  "<PoE1 conditions .cs>" \
  --poe2-scripts     "C:/Users/kjmik/Documents/Programming/Deadfire/PoE2 Code/Assembly-CSharp/Game/Scripts.cs" \
  --poe2-conditions  "C:/Users/kjmik/Documents/Programming/Deadfire/PoE2 Code/Assembly-CSharp/Game/Conditionals.cs" \
  --poe1-code        "C:/Users/kjmik/Documents/Programming/Deadfire/PoE1 Code/Assembly-CSharp" \
  --poe2-code        "C:/Users/kjmik/Documents/Programming/Deadfire/PoE2 Code/Assembly-CSharp" \
  --bundles          "D:/Program Files (x86)/GOG Galaxy/Games/Pillars of Eternity II Deadfire/PillarsOfEternityII_Data/exported/design/gamedata" \
  --conversations    "D:/Program Files (x86)/GOG Galaxy/Games/Pillars of Eternity II Deadfire/PillarsOfEternityII_Data/exported/design/conversations" \
  --repo             "C:/Users/kjmik/Documents/Programming/Deadfire/Dialog Editor"
```

Inputs are local-only (not in the repo / CI). The committed JSON + coverage fixture are the
CI-visible artifacts; `DialogEditor.Tests` `CatalogueCoverageTests` enforces coverage.

## Tests

```
python tools/catalogue-gen/test_generate.py
```
