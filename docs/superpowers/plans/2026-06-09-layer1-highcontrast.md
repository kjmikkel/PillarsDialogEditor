# Layer 1 High-Contrast Palette Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a faithful WCAG-AAA High-Contrast palette, enabled by splitting border and control-background tokens off the surface neutrals they share.

**Architecture:** First a pure-plumbing split — new `Palette.Line.*` and `Palette.Control.Background` primitives backing the `Border.*` and toolbar-button-background tokens, with Dark/Light/Colourblind values equal to today's so those three palettes stay byte-identical. Then author `Palette.HighContrast.axaml` (near-black surfaces, bright lines, white text, white node-card bodies, bright accents), gated by the existing contrast harness at the AAA bar plus a dedicated border-visibility test.

**Tech Stack:** Avalonia 11 XAML `ResourceDictionary`, C#/.NET 8, xUnit + `Avalonia.Headless.XUnit`.

**Spec:** `docs/superpowers/specs/2026-06-09-layer1-highcontrast-design.md`

**Starting state:** three palettes (`Palette.Dark/Light/Colourblind.axaml`); `PaletteHarness.AllSets` = {Dark, Light, Colourblind}, `EnforcedSets` = {Light, Colourblind}; `PaletteSetParityTests` and `PaletteContrastTests` each have Light + Colourblind `[InlineData]`; `palette-golden.approved.txt` covers 3 palettes. `Tokens.axaml` maps `Border.Subtle/Default/Strong/Muted` → `Neutral.175/200/265/335` and `Toolbar.Button.Background` → `Neutral.200`.

**Conventions:** tests run serially; run one class with `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.<Class>"`; commit on `main`; end commit bodies with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Golden updates: run the golden filter (fails, writes `palette-golden.received.txt`), `Copy-Item` received over approved, re-run (passes, auto-deletes received).

---

## Task 1: Token split (plumbing — Dark/Light/Colourblind stay byte-identical)

**Files:**
- Modify: `DialogEditor.Avalonia.Shared/Resources/Palette.Dark.axaml`, `Palette.Light.axaml`, `Palette.Colourblind.axaml`
- Modify: `DialogEditor.Avalonia.Shared/Resources/Tokens.axaml`
- Modify: `DialogEditor.Tests/Theming/palette-golden.approved.txt`

- [ ] **Step 1: Add the 5 new primitives to each of the three palette files**

In `Palette.Dark.axaml` and `Palette.Colourblind.axaml`, add (after the `Border`-related neutrals / near the accents; placement is cosmetic — they must simply exist):

```xml
    <!--  LINE / CONTROL — border-divider and interactive-control-background primitives, split off the
          shared neutrals so a High-Contrast palette can make borders bright + controls visible while
          surfaces go near-black (spec 2026-06-09-layer1-highcontrast-design.md §3). Dark/Colourblind
          values equal the neutral they replaced, so those palettes are unchanged on screen. -->
    <Color x:Key="Palette.Line.Subtle">#FF2D2D2D</Color>
    <Color x:Key="Palette.Line.Default">#FF333333</Color>
    <Color x:Key="Palette.Line.Strong">#FF444444</Color>
    <Color x:Key="Palette.Line.Muted">#FF555555</Color>
    <Color x:Key="Palette.Control.Background">#FF333333</Color>
```

In `Palette.Light.axaml`, add the same five keys with **Light's current** border/header values (so Light is unchanged):

```xml
    <!--  LINE / CONTROL — see spec §3. Light values equal Light's existing border/header greys. -->
    <Color x:Key="Palette.Line.Subtle">#FFDEDEDC</Color>
    <Color x:Key="Palette.Line.Default">#FFD6D6D4</Color>
    <Color x:Key="Palette.Line.Strong">#FFC2C2C0</Color>
    <Color x:Key="Palette.Line.Muted">#FF9A9A9A</Color>
    <Color x:Key="Palette.Control.Background">#FFD6D6D4</Color>
```

- [ ] **Step 2: Re-point the 5 tokens in `Tokens.axaml`**

Change the four `Border.*` brushes and `Toolbar.Button.Background`:

```xml
    <SolidColorBrush x:Key="Brush.Border.Default" Color="{StaticResource Palette.Line.Default}"/>
    <SolidColorBrush x:Key="Brush.Border.Subtle"  Color="{StaticResource Palette.Line.Subtle}"/>
    <SolidColorBrush x:Key="Brush.Border.Strong"  Color="{StaticResource Palette.Line.Strong}"/>
    <SolidColorBrush x:Key="Brush.Border.Muted"   Color="{StaticResource Palette.Line.Muted}"/>
```
and
```xml
    <SolidColorBrush x:Key="Brush.Toolbar.Button.Background"   Color="{StaticResource Palette.Control.Background}"/>
```
Append to the Borders comment: " Border.* and Toolbar.Button.Background re-pointed to the Palette.Line.*/Control.* split for High-Contrast (spec 2026-06-09 §3)."

- [ ] **Step 3: Verify resolved colours are unchanged (suite green before golden update)**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.TokenRegistryTests"`
Expected: PASS — `Brush.Border.Default` and `Brush.Toolbar.Button.Background` still resolve to `#333333` (Line.Default / Control.Background are `#333333` in Dark).

- [ ] **Step 4: Run parity (new keys exist in all three palettes)**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteSetParityTests"`
Expected: PASS — all three palettes gained the same five keys.

- [ ] **Step 5: Regenerate the golden snapshot**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteGoldenTests"
Copy-Item "DialogEditor.Tests/Theming/palette-golden.received.txt" "DialogEditor.Tests/Theming/palette-golden.approved.txt"
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteGoldenTests"
```
Expected: second run PASS. The diff adds the 5 new keys × 3 palettes; no existing value changes.

- [ ] **Step 6: Full suite + commit**

Run: `dotnet test DialogEditor.Tests` → expected PASS (all green; nothing changed on screen).

```bash
git add -A
git commit -m "refactor(theming): split Border.*/Toolbar bg onto Palette.Line.*/Control.* (no visual change)"
```

---

## Task 2: Re-add the High-Contrast skeleton

**Files:**
- Create: `DialogEditor.Avalonia.Shared/Resources/Palette.HighContrast.axaml`
- Modify: `DialogEditor.Tests/Theming/PaletteHarness.cs`
- Modify: `DialogEditor.Tests/Theming/PaletteSetParityTests.cs`
- Modify: `DialogEditor.Tests/Theming/palette-golden.approved.txt`

- [ ] **Step 1: Create the skeleton as a copy of Dark**

```powershell
Copy-Item "DialogEditor.Avalonia.Shared/Resources/Palette.Dark.axaml" "DialogEditor.Avalonia.Shared/Resources/Palette.HighContrast.axaml"
```
Then replace the top header comment's first line with:
```xml
<!--  PALETTE — HIGH-CONTRAST set (Layer 1). Same keys as Palette.Dark.axaml; skeleton = Dark values
      until the HC-authoring task fills them. See spec 2026-06-09-layer1-highcontrast-design.md §4. -->
```

- [ ] **Step 2: Re-enable HC in the harness**

In `DialogEditor.Tests/Theming/PaletteHarness.cs`, add `"Palette.HighContrast"` back to `AllSets` (after `Palette.Light`) and to `EnforcedSets`.

- [ ] **Step 3: Re-enable HC parity**

In `DialogEditor.Tests/Theming/PaletteSetParityTests.cs`, add `[InlineData("Palette.HighContrast")]`.

- [ ] **Step 4: Run parity**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteSetParityTests"`
Expected: PASS (3 cases) — the copy has Dark's exact key set.

- [ ] **Step 5: Regenerate golden (now 4 palettes)**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteGoldenTests"
Copy-Item "DialogEditor.Tests/Theming/palette-golden.received.txt" "DialogEditor.Tests/Theming/palette-golden.approved.txt"
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteGoldenTests"
```
Expected: second run PASS — adds a HighContrast block (currently = Dark values).

- [ ] **Step 6: Full suite + commit**

Run: `dotnet test DialogEditor.Tests` → PASS. (No contrast gate on HC yet, so the Dark-valued skeleton is fine.)

```bash
git add -A
git commit -m "test(theming): re-add High-Contrast skeleton (Dark-valued) + parity/golden"
```

---

## Task 3: Author the High-Contrast values + AAA / border gates (interactive)

**Files:**
- Modify: `DialogEditor.Tests/Theming/PaletteContrastTests.cs`
- Modify: `DialogEditor.Avalonia.Shared/Resources/Palette.HighContrast.axaml`
- Modify: `DialogEditor.Tests/Theming/palette-golden.approved.txt`

- [ ] **Step 1: Add the AAA contrast case + the border-visibility test**

In `PaletteContrastTests.cs`, add to the `PaletteMeetsContrastTargets` theory:
```csharp
    [InlineData("Palette.HighContrast", 7.0, 4.5)]
```
Then add a new test in the same class (its own method — borders are not in the shared `Pairs` list because Light/Dark borders are intentionally low-contrast):
```csharp
    private static readonly (string Line, string Surface)[] HcBorderPairs =
    {
        ("Palette.Line.Subtle",  "Palette.Neutral.115"), // Border.Subtle on Surface.Window
        ("Palette.Line.Subtle",  "Palette.Neutral.100"), // on Surface.Card
        ("Palette.Line.Default", "Palette.Neutral.115"),
        ("Palette.Line.Default", "Palette.Neutral.145"), // on Surface.Panel
        ("Palette.Line.Strong",  "Palette.Neutral.115"),
    };

    [AvaloniaFact]
    public void HighContrastBordersAreVisible()
    {
        var dict = PaletteHarness.Load("Palette.HighContrast");
        var failures = new System.Collections.Generic.List<string>();
        foreach (var (line, surface) in HcBorderPairs)
        {
            var ratio = Wcag.ContrastRatio(
                PaletteHarness.Color(dict, line), PaletteHarness.Color(dict, surface));
            if (ratio < 4.5)
                failures.Add($"{line} on {surface}: {ratio:F2}:1 < 4.5:1");
        }
        Assert.True(failures.Count == 0,
            "High-Contrast borders must be visible (>=4.5:1):\n" + string.Join("\n", failures));
    }
```

- [ ] **Step 2: Run to verify both fail (skeleton = Dark values)**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteContrastTests"`
Expected: FAIL — `Palette.HighContrast` misses AAA on most text pairs, and `HighContrastBordersAreVisible` fails (Line.* are dark greys on dark surfaces). The messages list the offending pairs/ratios.

- [ ] **Step 3: Author the HC values**

Edit `Palette.HighContrast.axaml`. Apply the §4 strategy; these are the intended values (tune only if a gate reports a miss — do not weaken a gate). Surfaces → black, lines bright, controls visible, text white, node cards white + black ink, accents bright, headers deepened for white text:

```xml
<!-- surfaces -> black -->
<Color x:Key="Palette.Neutral.115">#FF000000</Color>  <!-- window -->
<Color x:Key="Palette.Neutral.145">#FF000000</Color>  <!-- panel -->
<Color x:Key="Palette.Neutral.100">#FF000000</Color>  <!-- card -->
<Color x:Key="Palette.Neutral.80">#FF000000</Color>   <!-- input -->
<Color x:Key="Palette.Neutral.125">#FF000000</Color>  <!-- inset -->
<Color x:Key="Palette.Neutral.175">#FF000000</Color>  <!-- subtle surface -->
<Color x:Key="Palette.Neutral.200">#FF000000</Color>  <!-- header surface -->
<Color x:Key="Palette.Navy.100">#FF000000</Color>     <!-- info window bg -->
<!-- lines -> bright (gated >=4.5 on black) -->
<Color x:Key="Palette.Line.Subtle">#FF8C8C8C</Color>
<Color x:Key="Palette.Line.Default">#FFA8A8A8</Color>
<Color x:Key="Palette.Line.Strong">#FFFFFFFF</Color>
<Color x:Key="Palette.Line.Muted">#FF6A6A6A</Color>
<!-- control backgrounds -> visible mid-greys (white icons >=7) -->
<Color x:Key="Palette.Control.Background">#FF4D4D4D</Color>
<Color x:Key="Palette.Neutral.265">#FF5D5D5D</Color>  <!-- toolbar hover / checkedhover -->
<Color x:Key="Palette.Neutral.290">#FF6D6D6D</Color>  <!-- toolbar pressed -->
<Color x:Key="Palette.Neutral.225">#FF6D6D6D</Color>  <!-- toolbar checked -->
<!-- text ramp -> white/light -->
<Color x:Key="Palette.Neutral.910">#FFFFFFFF</Color>
<Color x:Key="Palette.Neutral.865">#FFF0F0F0</Color>
<Color x:Key="Palette.Neutral.800">#FFE6E6E6</Color>
<Color x:Key="Palette.Neutral.735">#FFD6D6D6</Color>
<Color x:Key="Palette.Neutral.665">#FFFFFFFF</Color>  <!-- toolbar fg / muted.light / connection -->
<Color x:Key="Palette.Neutral.600">#FFCCCCCC</Color>
<Color x:Key="Palette.Neutral.535">#FFC4C4C4</Color>
<Color x:Key="Palette.Neutral.400">#FF8A8A8A</Color>  <!-- disabled (ungated) -->
<Color x:Key="Palette.Neutral.335">#FF9A9A9A</Color>  <!-- border.muted / female.dim / connection.never -->
<!-- node card ink + bodies (light cards, black text) -->
<Color x:Key="Palette.Ink.Strong">#FF000000</Color>
<Color x:Key="Palette.Ink.Muted">#FF2A2A2A</Color>
<Color x:Key="Palette.Parchment.100">#FFFFFFFF</Color>
<Color x:Key="Palette.Parchment.200">#FFF0F0F0</Color>
<Color x:Key="Palette.Azure.150">#FFFFFFFF</Color>
<Color x:Key="Palette.Azure.250">#FFF0F0F0</Color>
<Color x:Key="Palette.Teal.150">#FFFFFFFF</Color>
<Color x:Key="Palette.Teal.250">#FFF0F0F0</Color>
<Color x:Key="Palette.Cream.100">#FFFFFFFF</Color>
<Color x:Key="Palette.Neutral.875">#FFFFFFFF</Color> <!-- script body -->
<Color x:Key="Palette.Neutral.785">#FFF0F0F0</Color> <!-- script footer -->
<!-- node headers (white text >=7): deepen Teal; keep Crimson/Azure/Slate -->
<Color x:Key="Palette.Teal.600">#FF0A4A3E</Color>
<!-- bark (white/ bright text >=7) -->
<Color x:Key="Palette.Amber.900">#FF5C4500</Color>  <!-- bark header/border -->
<Color x:Key="Palette.Amber.950">#FF1A1400</Color>  <!-- bark detail bg -->
<Color x:Key="Palette.Amber.560">#FFFFD24D</Color>  <!-- bark detail text / footer -->
<Color x:Key="Palette.Amber.570">#FFFFE08A</Color>
<Color x:Key="Palette.Amber.520">#FFFFB000</Color>  <!-- bark outline / highlight -->
<!-- status / diff / severity / syntax / links -> bright (>=7 on black) -->
<Color x:Key="Palette.Green.400">#FF5BFF8F</Color>
<Color x:Key="Palette.Green.500">#FF5BFF8F</Color>
<Color x:Key="Palette.Green.550">#FF5BFF8F</Color>  <!-- diff added fill (bright; black node text) -->
<Color x:Key="Palette.Amber.540">#FFFFD23B</Color>
<Color x:Key="Palette.Amber.500">#FFFFC04D</Color>
<Color x:Key="Palette.Amber.600">#FFFFD23B</Color>
<Color x:Key="Palette.Amber.610">#FFFFD23B</Color>  <!-- diff changed fill -->
<Color x:Key="Palette.Amber.550">#FFFFD24D</Color>
<Color x:Key="Palette.Red.450">#FFFF8080</Color>
<Color x:Key="Palette.Red.500">#FFFF6B6B</Color>
<Color x:Key="Palette.Red.550">#FFFF8080</Color>
<Color x:Key="Palette.Maroon.800">#FFFF8080</Color> <!-- diff removed fill -->
<Color x:Key="Palette.Sky.450">#FF6BB6FF</Color>
<Color x:Key="Palette.Sky.400">#FF6BB6FF</Color>
<Color x:Key="Palette.Sky.350">#FF9CC4FF</Color>
<Color x:Key="Palette.Sky.250">#FF9CDCFE</Color>
<Color x:Key="Palette.Olive.500">#FFC8C860</Color>
<Color x:Key="Palette.Magenta.500">#FFFF5BD0</Color>
<!-- action buttons (white labels >=7): deepen confirm/caution; Azure/Crimson already pass -->
<Color x:Key="Palette.Green.600">#FF155018</Color>
<Color x:Key="Palette.Burnt.600">#FF6E3A0E</Color>
<!-- conflict panels (dark, bright fg) -->
<Color x:Key="Palette.Navy.150">#FF001A33</Color>
<Color x:Key="Palette.Sky.300">#FF8CC4FF</Color>
<Color x:Key="Palette.Maroon.900">#FF2A0000</Color>
<Color x:Key="Palette.Red.300">#FFFF9C9C</Color>
```
Leave unchanged (already fine on black): `Crimson.700`, `Azure.600`, `Slate.700`, `Mauve.500`, `Sky.*` not listed, the `Alpha.*` primitives, `Black`/`White`.

- [ ] **Step 4: Run the contrast tests until green**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteContrastTests"`
Expected: PASS — `PaletteMeetsContrastTargets` (Light, Colourblind, HighContrast@7/4.5) and `HighContrastBordersAreVisible`. If a specific pair misses, adjust **that** primitive brighter; never lower a threshold. Then confirm parity still green:
Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteSetParityTests"` → PASS.

- [ ] **Step 5: Regenerate golden + full suite**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteGoldenTests"
Copy-Item "DialogEditor.Tests/Theming/palette-golden.received.txt" "DialogEditor.Tests/Theming/palette-golden.approved.txt"
dotnet test DialogEditor.Tests
```
Expected: full suite PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(theming): author High-Contrast palette (WCAG AAA + gated border visibility)"
```

---

## Task 4: Rationale header + doc updates

**Files:**
- Modify: `DialogEditor.Avalonia.Shared/Resources/Palette.HighContrast.axaml` (header)
- Modify: `Gaps.md`
- Modify: `docs/superpowers/specs/2026-06-09-layer1-highcontrast-design.md` (status), `docs/superpowers/specs/2026-06-08-layer1-palette-sets-design.md` (status banner)

- [ ] **Step 1: Full rationale header on the HC file**

Replace the skeleton one-liner with a full header: purpose; the §4 AAA aim; that surfaces are black and structure is carried by the bright `Line.*` borders + visible `Control.*` backgrounds (the split, §3); node cards are white with black `Ink`; keys matched by `PaletteSetParityTests`, values locked by `PaletteGoldenTests`, gated by `PaletteContrastTests` + `HighContrastBordersAreVisible`; pointer to the spec.

- [ ] **Step 2: Update `Gaps.md`**

In the Layer 1 bullet, move High-Contrast from "deferred follow-up" to **implemented**: note `Palette.HighContrast.axaml` ships at WCAG AAA, enabled by the `Palette.Line.*`/`Control.Background` border/control split (the only further `Tokens.axaml` edits), with border visibility test-gated. Mention all four palettes now exist; runtime selection remains Layer 2.

- [ ] **Step 3: Update spec statuses**

Set this plan's spec status to "Implemented 2026-06-09". In `2026-06-08-layer1-palette-sets-design.md`, update the status banner to note High-Contrast has now shipped (no longer deferred).

- [ ] **Step 4: Full suite + commit**

Run: `dotnet test DialogEditor.Tests` → PASS.

```bash
git add -A
git commit -m "docs(theming): High-Contrast rationale header + Gaps/spec status updates"
```

---

## Done criteria
- `Palette.HighContrast.axaml` exists with the same key set as the others (parity), all values locked by golden.
- `Tokens.axaml` border/toolbar tokens resolve through `Palette.Line.*`/`Control.Background`; Dark/Light/Colourblind render byte-identical.
- `PaletteContrastTests` enforces AAA on HC text pairs; `HighContrastBordersAreVisible` enforces ≥4.5:1 lines on surfaces.
- HC merged nowhere — running app unchanged; runtime selection is Layer 2.
