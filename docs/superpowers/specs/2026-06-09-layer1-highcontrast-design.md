# Layer 1 — High-Contrast Palette (Design)

**Date:** 2026-06-09
**Status:** Design — awaiting review
**Gap:** `Gaps.md` → *Centralised UI Colour Tokens* (Layer 1 — Palette sets → High-Contrast follow-up)
**Builds on:** `docs/superpowers/specs/2026-06-08-layer1-palette-sets-design.md` (Layer 1: Light + Colourblind), `docs/superpowers/specs/2026-06-07-colour-token-taxonomy-design.md` (Layer 0)

**Scope:** the deferred **High-Contrast** palette, plus the token-structure split it requires.
Runtime selection remains **Layer 2** (out of scope — the palette is inert groundwork). Shape/icon
redundancy is **Layer 2.5** (out of scope).

> **Why a design doc *and* file comments:** as with the rest of Layer 1, the reasoning is mirrored as
> header comments in `Palette.HighContrast.axaml` and the new primitives, so the *why* is recoverable
> at the point of use. This doc is the long form.

---

## 1. Why HC needs more than a value swap

Light and Colourblind shipped as pure value re-mappings (plus two surgical `Ink`/`Amber.610` splits).
HC cannot, because Dark deliberately **conflates three semantic families onto the same mid-grey
neutral primitives** — coherent in Dark (they all want a dark grey), contradictory in HC (borders must
be *bright* while surfaces must be *near-black*, and border-less toolbar buttons must be *visible*):

| Primitive | Surface/control role (wants dark) | Border role (wants bright) |
|---|---|---|
| `Neutral.200` | `Surface.Header`, `Toolbar.Button.Background` | `Border.Default` |
| `Neutral.175` | `Surface.Subtle` | `Border.Subtle` |
| `Neutral.265` | `Toolbar.Button.Hover`, `Toolbar.Button.CheckedHover` | `Border.Strong` |
| `Neutral.335` | `Text.Female.Dim`, `Connection.Never` | `Border.Muted` |

The toolbar control theme (`App.axaml` `ToolbarPlainButton`) renders **no border**
(`BorderThickness=0`), so toolbar-button visibility depends on *background* contrast alone — a button
background cannot be pure-black in HC, yet it shares `Neutral.200` with `Surface.Header` (which wants
to be black) and `Border.Default` (which wants to be bright). One value cannot serve all three.

The fix is a **token-structure split**, not a value hack: give borders and interactive-control
backgrounds their own primitive families, distinct from surfaces.

## 2. Goals / non-goals

**Goals**

- Add a complete, faithful **High-Contrast** palette (`Palette.HighContrast.axaml`) targeting WCAG
  **AAA** for text and ≥4.5:1 for non-text UI (including borders).
- Perform the **border / control-background token split** that makes a faithful HC possible.
- Keep **Dark, Light, and Colourblind byte-identical** through the split (new primitives take each
  palette's current shared-neutral value).
- Re-enable HC across the existing Layer 1 tests and add a dedicated **border-visibility** gate.

**Non-goals**

- No runtime/`ThemeVariant` switching, no Settings UI (Layer 2). HC is merged nowhere; the running
  app is unchanged.
- No shape/icon redundancy (Layer 2.5).
- No consumer changes (views, converters, control themes). The toolbar control theme stays
  border-less; HC achieves toolbar visibility via the control-background value, not a new border.
- No visual change to Dark/Light/Colourblind.

## 3. Token structure — the split

Two new **named role-primitive families** (same pattern as `Palette.Ink.*`), defined by *use* rather
than ramp position. Dark/Light/Colourblind set them to their existing shared-neutral values, so those
palettes render identically; only HC supplies divergent values.

### 3.1 New primitives (Dark values — equal to today's shared neutral)
```
Palette.Line.Subtle        = #FF2D2D2D   (was Neutral.175 via Border.Subtle)
Palette.Line.Default       = #FF333333   (was Neutral.200 via Border.Default)
Palette.Line.Strong        = #FF444444   (was Neutral.265 via Border.Strong)
Palette.Line.Muted         = #FF555555   (was Neutral.335 via Border.Muted)
Palette.Control.Background  = #FF333333   (was Neutral.200 via Toolbar.Button.Background)
```

### 3.2 Token re-points in `Tokens.axaml` (5)
```
Brush.Border.Subtle             -> Palette.Line.Subtle
Brush.Border.Default            -> Palette.Line.Default
Brush.Border.Strong             -> Palette.Line.Strong
Brush.Border.Muted              -> Palette.Line.Muted
Brush.Toolbar.Button.Background  -> Palette.Control.Background
```

After this, each formerly-shared neutral has a single role family and can take its natural HC value:
`Neutral.200` → only `Surface.Header` (pure-black in HC); `Neutral.265/290/225` → only the toolbar
hover/pressed/checked *states* (visible mid-greys); `Neutral.175` → only `Surface.Subtle`
(pure-black); `Neutral.335` → only `Text.Female.Dim`/`Connection.Never` (visible mids).

This brings the Layer 1 totals to **8 re-pointed `Tokens.axaml` lines** and **8 new primitives**
(`Ink.Strong/Muted`, `Amber.610`, `Line.{Subtle,Default,Strong,Muted}`, `Control.Background`). No
other semantic token changes.

### 3.3 Parity consequence
Every palette must define the 5 new keys (parity). Dark/Colourblind set them equal to the neutral
they replaced (byte-identical look); Light sets them to its current border/header values (its
`Border.*` and `Toolbar.Button.Background` already resolved to light greys — those exact values move
onto the new keys, so Light is unchanged). HC sets the divergent values in §4.

## 4. HC value strategy & accessibility targets

**Aim: WCAG AAA — text ≥7:1; non-text UI (borders, control affordances) ≥4.5:1.** Full per-key hex is
implementation output (generated into the golden snapshot, driven by the contrast tests); this section
fixes the strategy and the gated targets.

- **Surfaces** → near-pure black (`Surface.Window/Panel/Card/Input/Inset/Subtle/Header` ≈ `#000000`).
  Region separation comes from the bright `Line.*` borders, not surface-tone steps.
- **Lines** are the structural workhorse and are **contrast-gated** (§5): `Line.Subtle ≈ #8C8C8C`,
  `Line.Default ≈ #A8A8A8`, `Line.Strong = #FFFFFF`, `Line.Muted ≈ #6A6A6A` (Muted ungated).
- **Control backgrounds** visible so border-less toolbar buttons read: `Control.Background ≈ #4D4D4D`
  with white icons (`Toolbar.Button.Foreground → #FFFFFF`) at ≥7:1; hover/pressed/checked
  progressively lighter mid-greys for state feedback.
- **Text** ramp compresses toward white: `Text.Primary #FFFFFF`; caption/muted kept light enough for
  ≥7:1 on black. (`Text.Disabled` is the one role that may sit below 7 by design — it must *look*
  disabled; it is not in the gated set.)
- **Node cards** stay light cards taken to the extreme: bodies → white, `Ink.Strong/Muted` → black /
  near-black; header *fills* are kept (no consumer change) and deepened where needed so white header
  text clears 7:1 (e.g. `Teal.600`).
- **Accents** (status/diff/severity/syntax/link) → bright tints, each ≥7:1 on black; **action button**
  backgrounds deepened so white labels clear 7:1; **conflict** panels stay dark with bright blue/red
  foregrounds (already CVD-safe).

## 5. Testing & enforcement (TDD)

- **Re-enable HC** in the existing data-driven tests: add `"Palette.HighContrast"` to
  `PaletteHarness.AllSets` and `EnforcedSets`; add `[InlineData("Palette.HighContrast")]` to
  `PaletteSetParityTests`; add `[InlineData("Palette.HighContrast", 7.0, 4.5)]` to
  `PaletteContrastTests` (the curated text pairs at the AAA bar).
- **New `HighContrastBordersAreVisible` test:** asserts `Line.Subtle/Default/Strong` on
  `Surface.Window`, `Surface.Panel`, `Surface.Card` each ≥4.5:1. This lives in its own test, **not**
  the shared pair list — Light/Dark borders are intentionally low-contrast and would fail there; border
  visibility is an HC-specific guarantee. It is the property the whole split exists to deliver, so it
  is regression-locked.
- **Golden snapshot** regenerates to 4 palettes again, now including the `Line.*`/`Control.Background`
  keys across all palettes.
- The split step must keep **all existing tests green with zero value change** — `TokenRegistryTests`
  already asserts `Brush.Border.Default` and `Brush.Toolbar.Button.Background` == `#333333`, which
  stays true because `Line.Default`/`Control.Background` are `#333333` in Dark.

## 6. Build order

1. **Token split (plumbing).** Add the 5 new primitives to `Palette.Dark/Light/Colourblind.axaml`
   (each = its current shared-neutral value); re-point the 5 tokens in `Tokens.axaml`; regenerate the
   golden snapshot. Dark/Light/Colourblind render identically; full suite green. *(Mechanical.)*
2. **Author HC.** Create `Palette.HighContrast.axaml` (full key set); re-enable HC in
   parity/golden/contrast; add `HighContrastBordersAreVisible`; author values to clear AAA text + the
   border-visibility gate; regenerate golden. *(Interactive — preview + tune.)*
3. **Docs.** Rationale header in `Palette.HighContrast.axaml`; `Gaps.md` (HC → implemented);
   update the Layer 1 spec + this spec's status.

## 7. Open questions

None. Per-key HC hex is deferred to implementation by design (driven by the contrast gates), not
because it is undecided in principle.
