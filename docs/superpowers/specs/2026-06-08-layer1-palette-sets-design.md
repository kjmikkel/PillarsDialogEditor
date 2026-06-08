# Centralised UI Colour Tokens — Layer 1: Palette Sets (Design)

**Date:** 2026-06-08
**Status:** Design — awaiting review
**Gap:** `Gaps.md` → *Centralised UI Colour Tokens* (Layer 1 — Palette sets)
**Builds on:** `docs/superpowers/specs/2026-06-07-colour-token-taxonomy-design.md` (Layer 0)

**Scope of this spec:** Layer 1 **only** — defining alternate *value sets* for the existing
`Palette.*` primitive keys (Light, High-Contrast, Colourblind-tuned) alongside the current Dark
set. **Runtime selection / switching is Layer 2 and explicitly out of scope** — the new palettes
are inert groundwork, merged nowhere, until Layer 2 wires a chooser. Shape/icon redundancy for
colour-deficient users is **Layer 2.5** and out of scope here.

> **Why a design doc *and* file comments:** as with Layer 0, the reasoning below is mirrored as
> header comments in each generated palette file so the *why* is recoverable at the point of use.
> This doc is the long form; the file headers point back to it. Keep the two in sync.

---

## 1. Premise (what Layer 0 already guarantees)

Layer 0 split colour into two tiers: a private primitive tier (`Palette.*` `<Color>` resources,
the only place a hex literal may live) and a public semantic tier (`Brush.*` `<SolidColorBrush>`
resources in `Tokens.axaml`, each referencing exactly one primitive via `{StaticResource}`). Every
consumer binds `{DynamicResource Brush.*}`. Layer 0's spec §3 states the seam Layer 1 fills
verbatim:

> *A Layer 1 palette later = an alternate `Palette.axaml` with the same keys, different values;
> `Tokens.axaml` never changes. That is the whole point of the split.*

Layer 1 is the realisation of exactly that sentence, three times over.

## 2. Goals / non-goals

**Goals**

- Add three complete alternate primitive sets — **Light**, **High-Contrast**, **Colourblind-tuned**
  — each defining the *identical* `Palette.*` key set as Dark, with different values.
- Keep `Tokens.axaml` **frozen** (untouched). No view, converter, or semantic token changes.
- Keep the running app **unchanged** — the new sets are merged nowhere until Layer 2.
- Make accessibility a **tested contract**, not aspiration: structural parity, golden values, and
  WCAG contrast ratios enforced per palette at the token/value level.
- Rename the Dark set to `Palette.Dark.axaml` so all four sets read symmetrically.

**Non-goals**

- No runtime palette/`ThemeVariant` switching, no Settings UI, no "apply at startup" hook (Layer 2).
- No shape/icon/border redundancy encoding for colour-deficient users (Layer 2.5).
- No changes to `Tokens.axaml` or any *semantic consumer* (views, converters, code-behind). No new
  `Brush.*` keys, no new `Palette.*` keys — Layer 1 re-values the existing key set, it does not
  extend it. (The Dark-file rename does touch each app's `App.axaml` merge list — a resource-host
  edit, not a consumer/semantic change; see §3, §6.)
- No visual change to the shipping (Dark) app. The rename is value-preserving.

## 3. Architecture — sibling palette files, frozen Tokens, swap deferred

Approach chosen (over Avalonia `ThemeDictionaries` and single-file variants) because it is purely
additive, carries zero risk to the running app, defers all switching machinery to Layer 2 as
intended, and is precisely the seam Layer 0 documented.

```
DialogEditor.Avalonia.Shared/Resources/
  Palette.Dark.axaml          ← renamed from Palette.axaml; the baseline/default, still the ONLY one merged
  Palette.Light.axaml         ← new
  Palette.HighContrast.axaml  ← new
  Palette.Colourblind.axaml   ← new
  Tokens.axaml                ← UNCHANGED (frozen public contract)
```

**Invariants:**

- Each palette file declares the **exact same `Palette.*` key set** — same keys, different `<Color>`
  values, and nothing else (no `Brush.*`; those live only in `Tokens.axaml`).
- `Tokens.axaml` is not touched. Both apps' `App.axaml` keep merging only `Palette.Dark.axaml` +
  `Tokens.axaml`. The three new files are **inert** until Layer 2.
- A future Layer 2 swap = merge a different `Palette.<set>.axaml` in place of `Palette.Dark.axaml`
  (mechanism TBD in Layer 2: `ThemeVariant`, dictionary swap, or merge replacement).

### 3.1 `Neutral.*` is slot identity, not absolute lightness (revises Layer 0 §3.2)

Layer 0 §3.2 named `Palette.Neutral.<n>` as an *absolute* 0 (black) → 1000 (white) lightness rank.
Layer 1 must revise that: with `Tokens.axaml` frozen, a pure palette swap cannot re-map which slot
a token points at — so a slot's *value* must carry whatever lightness its semantic role needs in
each palette. Concretely, `Brush.Surface.Input → Palette.Neutral.80`; in Dark, `Neutral.80` is the
darkest neutral (recessed input), but in Light it must be **light** (a pale input surface).

Therefore `Neutral.<n>` denotes **slot identity** — "the recessed input/inset surface", "the
window surface", "the primary body-text neutral" — and the lightness ordering **inverts between
Dark and Light**. The number is a stable handle for the role across palettes; the value is free per
palette. (High-Contrast and Colourblind keep the Dark ordering; only Light inverts.) This is the
standard light/dark consequence of a frozen-semantic-layer design and was accepted during
brainstorming.

**Action:** update Layer 0 spec §3.2 to state this; the file header in `Palette.Dark.axaml` notes it
too.

### 3.2 Enforcer widening

`NoStrayHexTests.NoHexLiteralsOutsidePalette` (Layer 0) permits hex only where the filename ends
`Palette.axaml`. Layer 1 widens the allow-rule to the whole palette **family** via regex
`^Palette(\.[A-Za-z]+)?\.axaml$`, matching `Palette.Dark.axaml`, `Palette.Light.axaml`,
`Palette.HighContrast.axaml`, and `Palette.Colourblind.axaml`. Any *other* filename carrying a hex
literal still fails. The test's doc-comment ("hex primitives live ONLY in Palette.axaml") updates to
say "the palette family".

## 4. Per-palette derivation strategy & accessibility targets

The full `{key → hex}` table per palette is **implementation output**, generated into a reviewable
appendix the way Layer 0's migration inventory was — not hand-typed into this design. What this
section fixes is the *method* and the *target* each palette must satisfy, which the tests (§5)
enforce.

### 4.1 Light — aim: WCAG AA (4.5:1 normal text on surfaces, 3:1 large/non-text)

- **Neutrals invert by slot (§3.1):** `Surface.Window/Panel/Card/Input/Inset` become light greys
  (window an off-white, card white, input faintly tinted); `Border.*` become mid greys; the `Text.*`
  ramp flips to **dark-on-light** (`Text.Primary` near-black → `Text.Caption` mid-grey).
- **Node-card bodies survive largely intact:** they are *already* light tints (parchment/azure/teal)
  in Dark and their text already binds `Brush.Text.OnLight` (dark). Headers darken slightly so white
  header text stays legible.
- **Accents keep hue, drop lightness / gain saturation** so status/diff/severity/syntax colours read
  on light surfaces.

### 4.2 High-Contrast — aim: WCAG AAA (≥7:1 text, ≥4.5:1 large/non-text); the low-vision set

- Surfaces collapse toward **near-pure black**; `Text.Primary` → white.
- **Structure carried by borders, not fills:** `Border.*` become bright hairlines throughout.
- **Node headers become bright coloured text on black with a bright divider** rather than coloured
  fills (a coloured fill would sink the header text's contrast below AAA).
- Accents go fully saturated/bright, each verified ≥7:1 against the black background.
- Keeps the Dark slot lightness ordering (does not invert).

### 4.3 Colourblind-tuned — aim: hue pairs distinct under deuteranopia/protanopia/tritanopia; Dark-level contrast

- **Inherits the Dark set wholesale**, then remaps *only* the roles whose meaning rides on a
  red↔green(-ish) distinction, onto the **Okabe–Ito** colour-universal family:

  | Role group | Dark hue | Colourblind hue |
  |---|---|---|
  | `Diff.Added.Fill`, `Text.Status.Added/New/Success` | green | **blue `#0072B2`** |
  | `Diff.Changed.Fill`, `Text.Status.Changed`, `Severity.Warning`, `Connection.Always` | amber | **orange `#E69F00`** |
  | `Diff.Removed.Fill`, `Text.Status.Removed/Error`, `Severity.Error` | red | **vermillion `#D55E00`** |

  (Exact token→Okabe-Ito assignments, including whether fills vs text need distinct tints of the
  same safe hue for contrast, are settled in the implementation appendix; the three anchor hues above
  are fixed here.)
- **Left alone deliberately:**
  - `Conflict.Mine/Theirs` is already blue/red (CVD-safe by design per its `Tokens.axaml` comment).
  - Node speaker headers (crimson/azure/teal/slate/amber) stay — each node carries a redundant
    speaker-name text label, so header hue is not load-bearing. (Confirmed during brainstorming.)
- Keeps the Dark slot lightness ordering.

**Colourblind safety is by construction, not simulation (§5).**

## 5. Testing & enforcement (TDD — red/green per CLAUDE.md)

All written test-first; Layer 1 is "done" when they are green. A shared test helper merges
`Palette.<set>` + `Tokens.axaml` into a throwaway `ResourceDictionary` and resolves keys, so one
harness serves groups 2 and 3 and can load a non-default palette in isolation.

**Make the harness data-driven over a *list* of palettes** (parity, golden, and contrast all iterate
the same palette collection) so a future palette set is a single list entry, not new bespoke test
code. This keeps adding palettes append-only at the test level too — see §9.

1. **Structural parity (keystone).** Load all four palette dictionaries; assert each defines
   **exactly** the same `Palette.*` key set as `Palette.Dark.axaml` — no missing, no extra keys. This
   is what guarantees the frozen `Tokens.axaml` resolves cleanly under any palette. A missing key
   fails the build.

2. **Per-palette golden values.** A committed `{key → expected ARGB}` map for each of the three new
   palettes (Dark already has its Layer 0 golden). The reviewable source of truth; locks every value,
   including the Okabe–Ito remaps — which is what makes colourblind safety hold *by construction*
   (the safe values cannot silently drift). The spec/appendix records why each Okabe–Ito hex is the
   safe choice.

3. **Contrast assertions (enforced accessibility target).** For a curated list of foreground/background
   token pairs (§5.1), compute the WCAG 2.x contrast ratio from the resolved brush colours and assert
   a per-palette threshold:

   | Palette | Normal text | Large text / non-text UI |
   |---|---|---|
   | Dark, Light, Colourblind | ≥ 4.5 : 1 (AA) | ≥ 3 : 1 |
   | High-Contrast | ≥ 7 : 1 (AAA) | ≥ 4.5 : 1 |

   Verified at the **token/value level** (computed from resolved colours), not by rendering controls
   and sampling pixels — deterministic, simple, and it catches the real risk (a value that fails the
   ratio) rather than testing Avalonia's renderer.

4. **Enforcer widening.** Update `NoStrayHexTests` to the `^Palette(\.[A-Za-z]+)?\.axaml$` allow-rule
   (§3.2), keeping "hex only in the palette family, nowhere else" true across the new files.

### 5.1 Curated contrast pairs (locked)

The pairs cover the load-bearing combinations; each is checked in every palette against its §5
threshold (text pairs at the "normal text" bar unless marked *large/UI*).

- `Text.Primary` on `Surface.Window`, `Surface.Panel`, `Surface.Card`, `Surface.Input`
- `Text.Secondary` on `Surface.Panel`, `Surface.Card`
- `Text.Caption` / `Text.Muted` on `Surface.Card` *(large/UI bar — these are intentionally dim)*
- `Text.OnLight` on `Node.Npc.Body`, `Node.Player.Body`, `Node.Narrator.Body`
- `Text.OnAccent` on `Node.Npc.Header`, `Node.Player.Header`, `Node.Narrator.Header`,
  `Node.Script.Header`
- `Text.OnAccent` on `Button.Primary.Background`, `Button.Confirm.Background`,
  `Button.Destructive.Background`, `Button.Caution.Background`
- `Text.Status.Added`, `Text.Status.Changed`, `Text.Status.Removed` on `Surface.Card`
- `Severity.Warning`, `Severity.Error`, `Severity.Info` on `Surface.Panel` *(large/UI bar)*
- `Border.Default` / `Border.Strong` on `Surface.Window` *(non-text/UI bar — structural visibility)*

> For High-Contrast, node "header" pairs are evaluated as the **bright-text-on-black** treatment
> (§4.2), i.e. the header *text* token against `Surface.Window`, since HC headers are not fills.
> The implementation maps each pair to the concrete token actually painted in that palette.

## 6. Build order (for the implementation plan)

1. **Rename + enforcer (self-contained checkpoint).** `Palette.axaml` → `Palette.Dark.axaml`; update
   both apps' `App.axaml` `ResourceInclude`; widen `NoStrayHexTests` (regex + doc-comment + any
   `EndsWith("Palette.axaml")` reference). Pure rename — **all existing tests stay green, zero value
   change.**
2. **Test helper** — merge `Palette.<set>` + `Tokens` into an isolated `ResourceDictionary`; resolve
   keys. Serves parity, golden, contrast.
3. **Structural parity test** (red) → create the three palette files carrying the complete key set →
   green (skeletons established).
4. **Golden + contrast tests drive the values**, one palette at a time: **Light** (biggest inversion)
   → **High-Contrast** → **Colourblind** (smallest delta). Tests red; fill values til green; record
   the generated `{key → hex}` appendix per palette.
5. **Rationale headers** in each new palette file pointing back to this spec.

The three new files remain merged nowhere; `App.axaml` still loads only `Palette.Dark.axaml`.

## 7. Doc updates

- This spec (new).
- **Revise Layer 0 spec §3.2** — `Neutral.*` is slot identity; lightness flips per palette (§3.1).
- **`Gaps.md`** — update the Layer 1 entry (designed; points at this spec) and fix the Layer 0
  "only `Palette.axaml` permits a hex literal" wording to "the palette family"; reflect the
  `Palette.Dark.axaml` rename where Layer 0 text names the file.

## 8. Layer 2 hand-off note

When Layer 2 builds the palette chooser, it should **enumerate the available `Palette.*.axaml` files
dynamically** rather than hardcode the count or names of the four sets that exist today. Combined
with the data-driven test harness (§5) and the append-only file structure (§3), this makes adding a
future palette free at *every* level — file, test, and chooser. Do not bake "there are four
palettes" into Layer 2.

## 9. Future palette expansion (why deferral is safe)

Adding more palettes later — including **per-type colourblind sets** (separate deuteranopia /
protanopia / tritanopia tunings) — carries **no deferral penalty**: each is a purely additive
`Palette.<Set>.axaml` (full key set, one golden + contrast entry), picked up by the data-driven tests
(§5) and the family regex enforcer (§3.2). The per-palette cost is constant whenever it is paid.

Two deliberate consequences:

- **The single `Colourblind` set is intentional (YAGNI).** Okabe–Ito is *colour-universal* — designed
  to stay distinguishable under deuteranopia, protanopia, **and** tritanopia at once — so per-type
  palettes are a heavier, separate philosophy (three colourblind sets to keep contrast-valid, a
  busier chooser) for benefit Okabe–Ito is built to make unnecessary. Defer per-type sets until a
  real user shows the universal set fails them for a specific type.
- **The reusable one-time cost is already banked here.** The expensive, type-independent work is
  identifying *which token roles are colour-load-bearing* (the §4.3 remap table). That analysis is
  captured now and is reused unchanged by any future per-type palette; only the destination hues
  would differ.

## 10. Open questions

None at design time. The per-palette hex values are deferred to implementation by design (driven by
the golden + contrast tests), not because they are undecided in principle.
