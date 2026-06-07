# Centralised UI Colour Tokens — Layer 0 Taxonomy (Design)

**Date:** 2026-06-07
**Status:** Design — awaiting review
**Gap:** `Gaps.md` → *Centralised UI Colour Tokens* (Layer 0 — Token registry)
**Scope of this spec:** Layer 0 **only** — the token registry and the full migration
of every existing colour onto it. Layers 1 (palette sets), 2 (runtime switching) and
2.5 (redundant non-colour encoding) are explicitly **out of scope** and are only
referenced where Layer 0 must leave a clean seam for them.

> **Why a design doc *and* file comments:** the reasoning below is duplicated as
> header comments in the generated artifacts (`Palette.axaml`, `Tokens.axaml`, the
> converters, the enforcement test) so the *why* is recoverable at the point of use
> without reading this spec. This doc is the long form; the file comments are the
> pointer back to it. Keep the two in sync.

---

## 1. Problem (verified against the code, 2026-06-07)

Colour is currently hardcoded in three tiers with no single source of truth:

- **3 hardcoded greys** in `App.axaml`'s `ToolbarPlainButton` / `ToolbarPlainToggleButton`
  control themes (`#333`, `#aaa`, `#444`, `#4a4a4a`, `#3a3a3a`).
- **9 brush converters / controls** construct brushes in C# via `new SolidColorBrush(...)`,
  `Color.FromRgb`, or `Color.Parse`: `SpeakerCategoryToBrushConverter`, `NodeColorConverter`,
  `DiffStatusToBrushConverter`, `FlowIssueKindToSeverityBrushConverter`,
  `BoolToNewConversationBrushConverter`, `BoolToFemaleTextBrushConverter`,
  `PropertyValueStyleToBrushConverter`, plus `InlineDiffTextBlock` and
  `GitConflictResolutionWindow.axaml.cs`.
- **507 inline hex occurrences across 29 `.axaml` files** (60 distinct strings).

The duplication already drifts:

- `NodeColorConverter` lines 21–34 are a **verbatim copy** of
  `SpeakerCategoryToBrushConverter` lines 16–31 — the same 12 RGB triples — with the
  comment *"mirrors SpeakerCategoryToBrushConverter"* documenting the copy-paste
  instead of preventing it.
- The same *semantic role* has drifted into multiple values: "added/success" green is
  at least 4 colours (`#3a7a3a`, `#2d6a2d`, `#7dcea0`, `#5db55d`); "warning/changed"
  amber is ~8 (`#c08a2a`, `#b8760a`, `#e0a030`, `#e8a020`, `#f0a830`, `#f0ad4e`,
  `#e8c050`, …); "error/removed" red is ~6 (`#c0392b`, `#e74c3c`, `#e05555`, `#ff9c9c`,
  `#7a2a2a`, …). Each call site picked its own shade for the same meaning.

The app is hardcoded to `RequestedThemeVariant="Dark"` (stock `FluentTheme`); there is no
light/dark switch yet. Layer 0 does **not** add one — it makes one *possible* later.

## 2. Goals / non-goals

**Goals**

- One named-token registry; every colour in the app resolves a token, and **nothing
  constructs a colour any other way** (the gap's published contract).
- Kill the drift *by construction*: duplicated palettes become one shared key.
- Leave a clean seam for Layers 1/2 (palette swap, runtime switch) without building them.
- Reserve `Brush.Annotation.Region.*` keys for the Canvas Annotations gap.

**Non-goals**

- No light / high-contrast / colourblind palette (Layer 1).
- No settings UI / `ThemeVariant` switching (Layer 2).
- No redundant icon/border encoding (Layer 2.5).
- No visual redesign. The **only** intentional pixel changes are the drift-unification
  merges enumerated in §6, each individually listed and reviewable.

## 3. Architecture — two tiers, two dictionaries

```
Palette.<Family>.<Tone>                          ← PRIMITIVES (private)
   <Color> resources. The ONLY place a hex literal may appear.

Brush.<Domain>.<Subject>[.<Variant>][.<State>]   ← SEMANTICS (public contract)
   <SolidColorBrush> resources, each referencing exactly one Palette.* via {StaticResource}.
```

- **`Resources/Palette.axaml`** — primitives. Private by convention: nothing outside the
  registry references `Palette.*`.
- **`Resources/Tokens.axaml`** — semantics. The sole public interface every view,
  converter, and dependent gap binds to.
- Both merged in `App.axaml` **before** `FluentTheme` so app styles and converters resolve
  them.
- A Layer 1 palette later = an alternate `Palette.axaml` with the **same keys, different
  values**; `Tokens.axaml` never changes. That is the whole point of the split.

### 3.1 Why two-tier with the semantic layer as the *sole* public contract

- **Primitive ramp answers "what colours does this theme use?"** (the painter's palette).
- **Semantic layer answers "what does each colour mean?"** (the assignment).
- Splitting them means a future palette re-answers only the first question (re-point
  ~40 primitives once) instead of re-specifying every semantic role.
- Keeping primitives **private** stops a view from binding `Palette.Green.600` directly
  and re-coupling "is green" to "means added" — which is exactly the drift we are removing.

### 3.2 Why the neutral scale is abstract rank, not value-derived

`Palette.Neutral.<n>` numbers are an **abstract 0 (black) → 1000 (white) lightness rank**,
*not* the hex byte. A value-derived name (`Palette.Neutral.888`) becomes a **lie** the
moment Layer 1 re-values that slot (a high-contrast theme might make it darker). Abstract
rank encodes *position*, which survives re-valuing; the value is free to change per palette.
This is the same reason Tailwind/Material name slots 50–950 rather than by hex.

## 4. Naming grammar

- Dot-separated `PascalCase` segments, read left→right general→specific.
- **State is always the trailing segment**: `.Hover`, `.Pressed`, `.Checked`,
  `.CheckedHover`.
- Foreground vs background of the *same meaning* are **different tokens** (the carve-out):
  a legible foreground (`Brush.Text.Status.Added`) and a subtle background fill
  (`Brush.Diff.Added.Fill`) legitimately need different luminance and must not be merged.
- Two semantic tokens **may** point at the same primitive (e.g.
  `Brush.Text.Female.Active` and `Brush.Text.Primary` both → `Palette.Neutral.910`).
  That decouples *meaning* from *value* and is a feature, not redundancy.

## 5. Primitive ramp (`Palette.*`)

Tone numbers are abstract lightness rank (neutrals) or per-hue tone. Hex is the canonical
Dark value. "Absorbs" lists imperceptible/identical values folded in (see §6).

### 5.1 Neutrals (`Palette.Neutral.*`)

| Token | Hex | Absorbs | Typical role |
|---|---|---|---|
| `Palette.Neutral.80`  | `#141414` | | input background |
| `Palette.Neutral.100` | `#1a1a1a` | | card / inset background |
| `Palette.Neutral.115` | `#1e1e1e` | | window background |
| `Palette.Neutral.125` | `#202020` | | code/diff inset background |
| `Palette.Neutral.145` | `#252525` | | panel / dialog background |
| `Palette.Neutral.175` | `#2d2d2d` | | subtle surface / divider |
| `Palette.Neutral.200` | `#333333` | | default border / header background |
| `Palette.Neutral.225` | `#3a3a3a` | | toggle-checked background |
| `Palette.Neutral.265` | `#444444` | | input border / divider |
| `Palette.Neutral.290` | `#4a4a4a` | | button-pressed background |
| `Palette.Neutral.335` | `#555555` | | muted border / dim text |
| `Palette.Neutral.400` | `#666666` | | disabled text |
| `Palette.Neutral.535` | `#888888` | | muted / secondary text |
| `Palette.Neutral.600` | `#999999` | `#9a9a9a` | muted text (lighter) |
| `Palette.Neutral.665` | `#aaaaaa` | `#aaa` | secondary text |
| `Palette.Neutral.735` | `#bbbbbb` | | secondary text (emphasis) |
| `Palette.Neutral.785` | `#c8c8c8` | | script-card footer tint |
| `Palette.Neutral.800` | `#cccccc` | `#cfcfcf` | label / value text |
| `Palette.Neutral.865` | `#dddddd` | | emphasis text |
| `Palette.Neutral.875` | `#e0e0e0` | | script-card body tint |
| `Palette.Neutral.910` | `#e8e8e8` | `#eee` | primary body text |
| `Palette.Black`        | `#000000` | | base for shadows / scrims (alpha) |
| `Palette.White`        | `#ffffff` | | on-accent text / inset border (alpha) |

### 5.2 Accent hues

| Token | Hex | Absorbs | Typical role |
|---|---|---|---|
| `Palette.Crimson.700` | `#7b241c` | | NPC node header; destructive accent |
| `Palette.Maroon.800`  | `#7a2a2a` | | diff-removed canvas fill |
| `Palette.Maroon.900`  | `#3a1a1a` | | conflict "mine" background |
| `Palette.Red.500`     | `#c0392b` | | severity error |
| `Palette.Red.450`     | `#e74c3c` | | diff-removed text |
| `Palette.Red.550`     | `#e05555` | | inline "no match" error text |
| `Palette.Red.300`     | `#ff9c9c` | | conflict "mine" foreground |
| `Palette.Azure.600`   | `#1a5276` | | player header; primary action / focus accent |
| `Palette.Azure.150`   | `#d5e8f5` | | player-card body tint |
| `Palette.Azure.250`   | `#b0cde8` | | player-card footer tint |
| `Palette.Slate.700`   | `#2c3e50` | | script header |
| `Palette.Navy.100`    | `#1a1a2a` | | help/legend/about window background |
| `Palette.Navy.150`    | `#1a2738` | | conflict "theirs" background |
| `Palette.Sky.300`     | `#9cc4ff` | | conflict "theirs" foreground |
| `Palette.Sky.250`     | `#9cdcfe` | | code-value syntax text |
| `Palette.Sky.350`     | `#99aadd` | (`#9ad`) | changelog version link |
| `Palette.Sky.400`     | `#4a9eff` | | hyperlink |
| `Palette.Sky.450`     | `#5dade2` | | informational caption text |
| `Palette.Teal.600`    | `#0e6655` | | narrator header |
| `Palette.Teal.150`    | `#d5f0e8` | | narrator-card body tint |
| `Palette.Teal.250`    | `#b0e0d5` | | narrator-card footer tint |
| `Palette.Green.600`   | `#2d6a2d` | | confirm-button background |
| `Palette.Green.550`   | `#3a7a3a` | | diff-added canvas fill |
| `Palette.Green.500`   | `#5db55d` | | inline "replaced" success text ⚠ |
| `Palette.Green.400`   | `#7dcea0` | | new-conversation / added text; script syntax |
| `Palette.Amber.500`   | `#b8760a` | | severity warning |
| `Palette.Amber.600`   | `#c08a2a` | | diff-changed fill; commit-meta gold |
| `Palette.Amber.520`   | `#f0a830` | | bark outline; condition value; test-mode |
| `Palette.Amber.540`   | `#f0ad4e` | | diff-changed legend swatch ⚠ |
| `Palette.Amber.560`   | `#e8c050` | | bark text |
| `Palette.Amber.550`   | `#e8a020` | | condition syntax |
| ~~`Palette.Amber.530`~~ | ~~`#e0a030`~~ | → `Amber.540` | **absorbed** into `Brush.Text.Status.Changed` (§6/§8) |
| `Palette.Amber.900`   | `#7a5c00` | | bark border / header |
| `Palette.Amber.950`   | `#2a2000` | | bark background |
| `Palette.Olive.500`   | `#bdbd80` | | quotidian-note text |
| `Palette.Burnt.600`   | `#8e4912` | | caution-button background |
| `Palette.Parchment.100` | `#f5f0d0` | | NPC-card body tint |
| `Palette.Parchment.200` | `#e8e0b0` | | NPC-card footer tint |
| `Palette.Cream.100`   | `#fff8dc` | | bark-card body |
| `Palette.Mauve.500`   | `#7a6a8e` | | (ConversationView accent badge) |
| `Palette.Magenta.500` | `#ee00aa` | (`#e0a`) | About-window status text |

> ⚠ marks values flagged for the §8 review questions — candidate further unification.

## 6. Drift-unification ledger (the only intentional pixel changes)

Per the agreed policy (unify genuine same-role-same-context drift; keep foreground vs
background distinct). Each row is a deliberate, reviewable change. Everything **not** listed
here is preserved exactly.

**Imperceptible neutral merges (Δ ≤ 6/255 lightness — no meaningful change):**

| From | Into | Δ | Sites |
|---|---|---|---|
| `#9a9a9a` | `#999999` (`Neutral.600`) | 1 | BlameWindow ×6 |
| `#cfcfcf` | `#cccccc` (`Neutral.800`) | 3 | BlameWindow ×4, HistoryWindow ×2 |
| `#eee`    | `#e8e8e8` (`Neutral.910`) | 6 | AboutWindow ×1 |
| `#aaaaaa` | `#aaa` (identical)        | 0 | ConversationView ×1 |

**Accent role unifications (resolved 2026-06-07 — see §8):** the §8 judgement calls were
ruled on; only one accent merge results — the "changed" *text* drift:

| From | Into | Role | Sites |
|---|---|---|---|
| `#e0a030` | `#f0ad4e` (`Amber.540`) | `Brush.Text.Status.Changed` | DiffWindow ×2 |

All other accents are preserved as distinct primitives in §5 (greens kept distinct, the two
Sky blues kept distinct, `Severity.Info` reserved). `Palette.Amber.530` is therefore
absorbed and does **not** appear in the final registry.

## 7. Semantic token catalogue (`Brush.*` — the public contract)

### 7.1 Node cards — dissolves the `NodeColorConverter` / `SpeakerCategoryToBrushConverter` duplication
```
Brush.Node.Npc.Header        -> Palette.Crimson.700
Brush.Node.Npc.Body          -> Palette.Parchment.100
Brush.Node.Npc.Footer        -> Palette.Parchment.200
Brush.Node.Player.Header      -> Palette.Azure.600
Brush.Node.Player.Body        -> Palette.Azure.150
Brush.Node.Player.Footer      -> Palette.Azure.250
Brush.Node.Narrator.Header    -> Palette.Teal.600
Brush.Node.Narrator.Body      -> Palette.Teal.150
Brush.Node.Narrator.Footer    -> Palette.Teal.250
Brush.Node.Script.Header      -> Palette.Slate.700
Brush.Node.Script.Body        -> Palette.Neutral.875   (#e0e0e0)
Brush.Node.Script.Footer      -> Palette.Neutral.785   (#c8c8c8)
Brush.Node.Bark.Header        -> Palette.Amber.900
Brush.Node.Bark.Body          -> Palette.Cream.100
Brush.Node.Bark.Footer        -> Palette.Amber.560     (#e8d080 — see note)
```
> Bark footer in `NodeColorConverter` is `#E8D080`; if it is not otherwise present it gets
> its own `Palette.Amber.560` alias. Resolve during implementation against the exact
> converter bytes.

### 7.2 Diff (canvas background fills — from `DiffStatusToBrushConverter`)
```
Brush.Diff.Added.Fill    -> Palette.Green.550    (#3a7a3a)
Brush.Diff.Changed.Fill  -> Palette.Amber.600    (#c08a2a)
Brush.Diff.Removed.Fill  -> Palette.Maroon.800   (#7a2a2a)
```

### 7.3 Severity (Flow Analytics — `FlowIssueKindToSeverityBrushConverter`)
```
Brush.Severity.Warning   -> Palette.Amber.500    (#b8760a)
Brush.Severity.Error     -> Palette.Red.500      (#c0392b)
Brush.Severity.Info      -> Palette.Sky.450      (reserved; nearest existing info hue)
```

### 7.4 Toolbar buttons (`App.axaml` control themes)
```
Brush.Toolbar.Button.Background    -> Palette.Neutral.200   (#333)
Brush.Toolbar.Button.Foreground    -> Palette.Neutral.665   (#aaa)
Brush.Toolbar.Button.Hover         -> Palette.Neutral.265   (#444)
Brush.Toolbar.Button.Pressed       -> Palette.Neutral.290   (#4a4a4a)
Brush.Toolbar.Button.Checked       -> Palette.Neutral.225   (#3a3a3a)
Brush.Toolbar.Button.CheckedHover  -> Palette.Neutral.265   (#444)
```

### 7.5 Neutral surfaces / borders / text (the bulk of the 507)
```
Brush.Surface.Window     -> Palette.Neutral.115   (#1e1e1e)
Brush.Surface.Panel      -> Palette.Neutral.145   (#252525)
Brush.Surface.Card       -> Palette.Neutral.100   (#1a1a1a)
Brush.Surface.Input      -> Palette.Neutral.80    (#141414)
Brush.Surface.Inset      -> Palette.Neutral.125   (#202020)
Brush.Surface.Header     -> Palette.Neutral.200   (#333  as background)
Brush.Surface.Info       -> Palette.Navy.100      (#1a1a2a help/legend/about window)
Brush.Surface.Overlay.Scrim -> Palette.Black @ 0xBB   (#bb000000)
Brush.Effect.Shadow      -> Palette.Black @ 0xA0   (#a0000000)

Brush.Border.Default     -> Palette.Neutral.200   (#333  as BorderBrush)
Brush.Border.Subtle      -> Palette.Neutral.175   (#2d2d2d)
Brush.Border.Strong      -> Palette.Neutral.265   (#444)
Brush.Border.Muted       -> Palette.Neutral.335   (#555)
Brush.Border.OnDark      -> Palette.White @ 0x33   (#33ffffff)
Brush.Border.Focus       -> Palette.Azure.600     (#1a5276 — focus/primary accent)

Brush.Text.Primary       -> Palette.Neutral.910   (#e8e8e8)
Brush.Text.Emphasis      -> Palette.Neutral.865   (#ddd)
Brush.Text.Secondary     -> Palette.Neutral.800   (#ccc)
Brush.Text.Tertiary      -> Palette.Neutral.735   (#bbb)
Brush.Text.Muted.Light   -> Palette.Neutral.665   (#aaa)
Brush.Text.Caption       -> Palette.Neutral.600   (#999)
Brush.Text.Muted         -> Palette.Neutral.535   (#888)
Brush.Text.Disabled      -> Palette.Neutral.400   (#666)
Brush.Text.OnAccent      -> Palette.White         (#ffffff "White")
Brush.Text.Female.Active  -> Palette.Neutral.910   (#e8e8e8)
Brush.Text.Female.Dim     -> Palette.Neutral.335   (#555)
```
> The text ramp has seven used emphasis levels (Primary→Caption→Muted); each maps to a
> distinct existing grey, so none are merged. `Tertiary` (#bbb) and `Caption` (#999) were
> added during planning to cover greys §7's first draft missed.
> `#333` and `#555` are the genuinely dual-role greys. The **attribute disambiguates**:
> `Background="#333"` → `Brush.Surface.Header`; `BorderBrush="#333"` → `Brush.Border.Default`;
> `BorderBrush="#555"` → `Brush.Border.Muted`; `Foreground="#555"` → `Brush.Text.Female.Dim`
> (or `Brush.Text.Disabled` by context). The migration applies this rule per site; §9
> appendix carries the per-line attribute so the choice is mechanical.

### 7.6 Status foreground accents (legible-text side of the carve-out)
```
Brush.Text.Status.New      -> Palette.Green.400   (#7dcea0 new-conversation)
Brush.Text.Status.Added    -> Palette.Green.400   (#7dcea0 diff legend/text)
Brush.Text.Status.Changed  -> Palette.Amber.540   (#f0ad4e diff legend) ⚠
Brush.Text.Status.Removed   -> Palette.Red.450     (#e74c3c diff legend)
Brush.Text.Status.Success   -> Palette.Green.500   (#5db55d inline) ⚠
Brush.Text.Status.Error     -> Palette.Red.550     (#e05555 inline)
Brush.Text.Meta.Commit      -> Palette.Amber.600   (#c08a2a commit hash/author)
Brush.Text.Status.Pending    -> Palette.Magenta.500  (#ee00aa About-window status)
```

### 7.7 Syntax / parameter styling (`PropertyValueStyleToBrushConverter`)
```
Brush.Syntax.Condition   -> Palette.Amber.550   (#e8a020)
Brush.Syntax.Script      -> Palette.Green.400   (#7dcea0)
Brush.Syntax.Code        -> Palette.Sky.250     (#9cdcfe — distinct from Conflict.Theirs #9cc4ff)
Brush.Syntax.Default     -> Palette.Neutral.910 (#e8e8e8)
```

### 7.8 Merge-conflict mine/theirs pair (`GitConflictResolutionWindow`)
```
Brush.Conflict.Mine.Background     -> Palette.Maroon.900   (#3a1a1a)
Brush.Conflict.Mine.Foreground     -> Palette.Red.300      (#ff9c9c)
Brush.Conflict.Theirs.Background    -> Palette.Navy.150     (#1a2738)
Brush.Conflict.Theirs.Foreground    -> Palette.Sky.300      (#9cc4ff)
```

### 7.9 Action buttons (coloured, with white text)
```
Brush.Button.Confirm.Background  -> Palette.Green.600   (#2d6a2d Apply/Switch/Commit)
Brush.Button.Caution.Background   -> Palette.Burnt.600   (#8e4912 force/destructive-ish)
```

### 7.10 Bark detail block (`NodeDetailView` bark preview) + canvas bark outline
```
Brush.Bark.Detail.Background  -> Palette.Amber.950   (#2a2000)
Brush.Bark.Detail.Border      -> Palette.Amber.900   (#7a5c00)
Brush.Bark.Detail.Text        -> Palette.Amber.560   (#e8c050)
Brush.Node.Bark.Outline       -> Palette.Amber.520   (#f0a830 canvas bark node Stroke)
Brush.Node.Quotidian.Note     -> Palette.Olive.500   (#bdbd80)
```

### 7.11 Links, info text, highlight, badge (accent foregrounds)
```
Brush.Text.Link          -> Palette.Sky.400    (#4a9eff hyperlink)
Brush.Text.Link.Subtle    -> Palette.Sky.350    (#99aadd changelog version link)
Brush.Text.Info          -> Palette.Sky.450    (#5dade2 info caption; shares Severity.Info)
Brush.Text.Highlight     -> Palette.Amber.520  (#f0a830 condition value / test-mode text)
Brush.Accent.Badge       -> Palette.Mauve.500  (#7a6a8e ConversationView badge background)
```
> §7.10/§7.11 tokens (`Bark.Outline`, `Link*`, `Info`, `Highlight`, `Badge`) were added
> during planning to cover primitives §7's first draft listed but never assigned a token.

### 7.12 Reserved (declared, no values — for the Canvas Annotations gap)
```
Brush.Annotation.Region.*      -- reserved key namespace; populated when that feature lands.
```

## 8. Resolved decisions (judgement calls — ruled 2026-06-07)

All four resolved to their defaults:

1. **Status text greens** — `Brush.Text.Status.Success` (`#5db55d`, saturated) stays
   **distinct** from `Brush.Text.Status.Added`/`New` (`#7dcea0`, mint): different hues, not
   just lightness.
2. **Changed-amber text** — `#e0a030` (diff text) **collapses into** `#f0ad4e`
   (`Brush.Text.Status.Changed`, `Amber.540`); `#c08a2a` stays separate as the commit-meta
   gold (`Brush.Text.Meta.Commit`). The merge is recorded in §6.
3. **`Brush.Severity.Info`** is **reserved** pointing at `Palette.Sky.450` (no current call
   site; declared so the cluster is complete).
4. **`#9cdcfe` (Syntax.Code) vs `#9cc4ff` (Conflict.Theirs.Fg)** — kept as **separate** Sky
   tones (`Sky.250` vs `Sky.300`).

## 9. Migration & exhaustive occurrence inventory

The complete `file:line → value → target token` inventory for all 507 occurrences is
appended as **§Appendix A** (generated, not hand-typed, so it stays accurate). It doubles as
the implementation checklist: every row is one edit, and the §5/§7 tables + the §7.5
attribute rule determine each target token. Converter constants (the 9 C# files) are listed
in **§Appendix B**.

## 10. Resource mechanics

- **Primitive:** `<Color x:Key="Palette.Neutral.535">#FF888888</Color>` in `Palette.axaml`.
- **Semantic:** `<SolidColorBrush x:Key="Brush.Text.Muted" Color="{StaticResource Palette.Neutral.535}"/>`
  in `Tokens.axaml`. Alpha tokens carry ARGB directly
  (`Color="{StaticResource Palette.Black}"` + `Opacity`, or an explicit `#AARRGGBB` alias).
- **XAML consumers** bind with `{DynamicResource Brush.*}` (not `StaticResource`) so a future
  Layer 2 `ThemeVariant`/palette swap re-resolves live. That one keyword is the entire
  forward-compat hook.
- **Converters** stop holding RGB. Each maps its enum → a **token key string** and resolves
  through one shared helper:
  ```csharp
  // TokenBrushes.Resolve("Brush.Node.Npc.Header") reads Application.Current!.Resources.
  ```
  `SpeakerCategoryToBrushConverter` and `NodeColorConverter` map to the **same**
  `Brush.Node.*` keys — the duplicated RGB tables are deleted, replaced by key strings
  pointing at one shared definition. (Live re-resolution on a runtime swap is a Layer 2
  concern; noted, not built.)

## 11. Testing & enforcement (TDD — red/green per CLAUDE.md)

All four are written test-first; the migration is "done" when they are green.

1. **Palette golden test.** A committed `{token → expected ARGB}` map asserted against the
   loaded `Tokens.axaml`. The reviewable source of truth; locks every value including each
   §6 merge.
2. **No-stray-hex test (the contract enforcer).** Scans `.axaml` + converter source for hex /
   `Color.FromRgb` / `Color.Parse` literals **outside `Palette.axaml`**; fails if any exist.
   This makes "nothing constructs a colour any other way" *true* rather than aspirational,
   and is the migration's definition-of-done — you don't count 507 sites, you run the test.
3. **No-dangling-token test.** Load `Tokens.axaml`; assert every `Brush.*` resolves to a
   non-null brush and every referenced `Palette.*` exists.
4. **Converter-resolves-from-registry test.** For each converter × each enum value, assert the
   returned brush is the registry brush for the expected key.

`Brush.Annotation.Region.*` stays reserved/unpopulated (no test) until the Annotations gap.

## 12. Build order (for the implementation plan)

1. Create `Palette.axaml` (primitives, §5) — golden test #1 + #3 drive it.
2. Create `Tokens.axaml` (semantics, §7) referencing primitives — tests #1/#3 green.
3. Wire both into `App.axaml`; migrate the toolbar control themes (§7.4).
4. Convert the 9 brush converters to `TokenBrushes.Resolve` (§10) — test #4 drives it;
   delete the duplicated RGB tables.
5. Migrate the 29 `.axaml` files to `{DynamicResource Brush.*}` (Appendix A), applying the
   §7.5 attribute rule.
6. Add the no-stray-hex test (#2); fix the long tail until green.
7. Add header/rationale comments to `Palette.axaml`, `Tokens.axaml`, `TokenBrushes`, and the
   enforcement test pointing back to this spec.

---

## Appendix A — exhaustive `.axaml` occurrence inventory

*(Generated below from a full ripgrep sweep; each row = one edit. Target token follows §5/§7;
dual-role greys (`#333`, `#555`, `#444`) resolve by the §7.5 attribute rule and are marked
`[attr]`.)*

| File | Line | Value | Primitive | Note |
|---|---|---|---|---|
| App.axaml | 25 | `#333` | `Palette.Neutral.200` | [attr] |
| App.axaml | 26 | `#aaa` | `Palette.Neutral.665` |  |
| App.axaml | 45 | `#444` | `Palette.Neutral.265` | [attr] |
| App.axaml | 48 | `#4a4a4a` | `Palette.Neutral.290` |  |
| App.axaml | 53 | `#333` | `Palette.Neutral.200` | [attr] |
| App.axaml | 54 | `#aaa` | `Palette.Neutral.665` |  |
| App.axaml | 73 | `#444` | `Palette.Neutral.265` | [attr] |
| App.axaml | 76 | `#4a4a4a` | `Palette.Neutral.290` |  |
| App.axaml | 79 | `#3a3a3a` | `Palette.Neutral.225` |  |
| App.axaml | 82 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/AboutWindow.axaml | 7 | `#1a1a2a` | `Palette.Navy.100` |  |
| Views/AboutWindow.axaml | 9 | `#eee` | `Palette.Neutral.910` |  |
| Views/AboutWindow.axaml | 11 | `#888` | `Palette.Neutral.535` |  |
| Views/AboutWindow.axaml | 12 | `#ccc` | `Palette.Neutral.800` |  |
| Views/AboutWindow.axaml | 14 | `#bbb` | `Palette.Neutral.735` |  |
| Views/AboutWindow.axaml | 15 | `#999` | `Palette.Neutral.600` |  |
| Views/AboutWindow.axaml | 16 | `#999` | `Palette.Neutral.600` |  |
| Views/AboutWindow.axaml | 27 | `#e0a` | `Palette.Magenta.500` |  |
| Views/BatchReplaceWindow.axaml | 10 | `#1e1e1e` | `Palette.Neutral.115` |  |
| Views/BatchReplaceWindow.axaml | 16 | `#141414` | `Palette.Neutral.80` |  |
| Views/BatchReplaceWindow.axaml | 17 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/BatchReplaceWindow.axaml | 18 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/BatchReplaceWindow.axaml | 24 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/BatchReplaceWindow.axaml | 25 | `#ccc` | `Palette.Neutral.800` |  |
| Views/BatchReplaceWindow.axaml | 32 | `#1a5276` | `Palette.Azure.600` |  |
| Views/BatchReplaceWindow.axaml | 40 | `#888` | `Palette.Neutral.535` |  |
| Views/BatchReplaceWindow.axaml | 61 | `#aaa` | `Palette.Neutral.665` |  |
| Views/BatchReplaceWindow.axaml | 76 | `#888` | `Palette.Neutral.535` |  |
| Views/BatchReplaceWindow.axaml | 80 | `#ccc` | `Palette.Neutral.800` |  |
| Views/BatchReplaceWindow.axaml | 84 | `#ccc` | `Palette.Neutral.800` |  |
| Views/BatchReplaceWindow.axaml | 88 | `#ccc` | `Palette.Neutral.800` |  |
| Views/BatchReplaceWindow.axaml | 92 | `#ccc` | `Palette.Neutral.800` |  |
| Views/BatchReplaceWindow.axaml | 96 | `#ccc` | `Palette.Neutral.800` |  |
| Views/BatchReplaceWindow.axaml | 112 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/BatchReplaceWindow.axaml | 120 | `#252525` | `Palette.Neutral.145` |  |
| Views/BatchReplaceWindow.axaml | 132 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/BatchReplaceWindow.axaml | 137 | `#888` | `Palette.Neutral.535` |  |
| Views/BatchReplaceWindow.axaml | 148 | `#aaa` | `Palette.Neutral.665` |  |
| Views/BatchReplaceWindow.axaml | 152 | `#888` | `Palette.Neutral.535` |  |
| Views/BatchReplaceWindow.axaml | 156 | `#e05555` | `Palette.Red.550` |  |
| Views/BatchReplaceWindow.axaml | 162 | `#888` | `Palette.Neutral.535` |  |
| Views/BatchReplaceWindow.axaml | 166 | `#5db55d` | `Palette.Green.500` |  |
| Views/BatchReplaceWindow.axaml | 185 | `#888` | `Palette.Neutral.535` |  |
| Views/BranchNameDialog.axaml | 8 | `#252525` | `Palette.Neutral.145` |  |
| Views/BranchNameDialog.axaml | 11 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/BranchNameDialog.axaml | 13 | `#141414` | `Palette.Neutral.80` |  |
| Views/BranchNameDialog.axaml | 13 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/BranchNameDialog.axaml | 14 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/BranchNameDialog.axaml | 21 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/BranchNameDialog.axaml | 26 | `#1a5276` | `Palette.Azure.600` |  |
| Views/ChangelogWindow.axaml | 7 | `#1a1a2a` | `Palette.Navy.100` |  |
| Views/ChangelogWindow.axaml | 17 | `#bbb` | `Palette.Neutral.735` |  |
| Views/ChangelogWindow.axaml | 24 | `#ddd` | `Palette.Neutral.865` |  |
| Views/ChangelogWindow.axaml | 38 | `#9ad` | `Palette.Sky.350` |  |
| Views/ChangelogWindow.axaml | 43 | `#bbb` | `Palette.Neutral.735` |  |
| Views/BlameWindow.axaml | 8 | `#252525` | `Palette.Neutral.145` |  |
| Views/BlameWindow.axaml | 16 | `#9a9a9a` | `Palette.Neutral.600` |  |
| Views/BlameWindow.axaml | 18 | `#9a9a9a` | `Palette.Neutral.600` |  |
| Views/BlameWindow.axaml | 20 | `#9a9a9a` | `Palette.Neutral.600` |  |
| Views/BlameWindow.axaml | 22 | `#9a9a9a` | `Palette.Neutral.600` |  |
| Views/BlameWindow.axaml | 24 | `#9a9a9a` | `Palette.Neutral.600` |  |
| Views/BlameWindow.axaml | 26 | `#9a9a9a` | `Palette.Neutral.600` |  |
| Views/BlameWindow.axaml | 31 | `#c08a2a` | `Palette.Amber.600` |  |
| Views/BlameWindow.axaml | 37 | `#1a1a1a` | `Palette.Neutral.100` |  |
| Views/BlameWindow.axaml | 43 | `#cfcfcf` | `Palette.Neutral.800` |  |
| Views/BlameWindow.axaml | 45 | `#cfcfcf` | `Palette.Neutral.800` |  |
| Views/BlameWindow.axaml | 47 | `#cfcfcf` | `Palette.Neutral.800` |  |
| Views/BlameWindow.axaml | 51 | `#cfcfcf` | `Palette.Neutral.800` |  |
| Views/BlameWindow.axaml | 53 | `#888` | `Palette.Neutral.535` |  |
| Views/BlameWindow.axaml | 55 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/BlameWindow.axaml | 65 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/CommitConsentDialog.axaml | 9 | `#252525` | `Palette.Neutral.145` |  |
| Views/CommitConsentDialog.axaml | 12 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/CommitConsentDialog.axaml | 14 | `#33ffffff` | `Palette.White @ 0x33` |  |
| Views/CommitConsentDialog.axaml | 20 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/CommitConsentDialog.axaml | 27 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/CommitConsentDialog.axaml | 29 | `#141414` | `Palette.Neutral.80` |  |
| Views/CommitConsentDialog.axaml | 29 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/CommitConsentDialog.axaml | 30 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/CommitConsentDialog.axaml | 37 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/CommitConsentDialog.axaml | 42 | `#1a5276` | `Palette.Azure.600` |  |
| Views/ConditionEditorWindow.axaml | 11 | `#1e1e1e` | `Palette.Neutral.115` |  |
| Views/ConditionEditorWindow.axaml | 16 | `#888` | `Palette.Neutral.535` |  |
| Views/ConditionEditorWindow.axaml | 24 | `#888` | `Palette.Neutral.535` |  |
| Views/ConditionEditorWindow.axaml | 33 | `#ccc` | `Palette.Neutral.800` |  |
| Views/ConditionEditorWindow.axaml | 36 | `#141414` | `Palette.Neutral.80` |  |
| Views/ConditionEditorWindow.axaml | 37 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/ConditionEditorWindow.axaml | 38 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/ConditionEditorWindow.axaml | 45 | `#141414` | `Palette.Neutral.80` |  |
| Views/ConditionEditorWindow.axaml | 46 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/ConditionEditorWindow.axaml | 47 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/ConditionEditorWindow.axaml | 54 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/ConditionEditorWindow.axaml | 55 | `#ccc` | `Palette.Neutral.800` |  |
| Views/ConditionEditorWindow.axaml | 62 | `#1a5276` | `Palette.Azure.600` |  |
| Views/ConditionEditorWindow.axaml | 76 | `#252525` | `Palette.Neutral.145` |  |
| Views/ConditionEditorWindow.axaml | 86 | `#1a1a1a` | `Palette.Neutral.100` |  |
| Views/ConditionEditorWindow.axaml | 86 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/ConditionEditorWindow.axaml | 92 | `#aaa` | `Palette.Neutral.665` |  |
| Views/ConditionEditorWindow.axaml | 98 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/ConditionEditorWindow.axaml | 113 | `#f0a830` | `Palette.Amber.520` |  |
| Views/ConditionEditorWindow.axaml | 118 | `#888` | `Palette.Neutral.535` |  |
| Views/ConditionEditorWindow.axaml | 126 | `#2c3e50` | `Palette.Slate.700` |  |
| Views/ConditionEditorWindow.axaml | 126 | `#ccc` | `Palette.Neutral.800` |  |
| Views/ConditionEditorWindow.axaml | 137 | `#aaa` | `Palette.Neutral.665` |  |
| Views/ConditionEditorWindow.axaml | 145 | `#aaa` | `Palette.Neutral.665` |  |
| Views/ConditionEditorWindow.axaml | 153 | `#c0392b` | `Palette.Red.500` |  |
| Views/ConditionEditorWindow.axaml | 202 | `#1a1a1a` | `Palette.Neutral.100` |  |
| Views/ConditionEditorWindow.axaml | 208 | `#141414` | `Palette.Neutral.80` |  |
| Views/ConditionEditorWindow.axaml | 208 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/ConditionEditorWindow.axaml | 215 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/ConditionEditorWindow.axaml | 217 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/ConditionEditorWindow.axaml | 226 | `#2c3e50` | `Palette.Slate.700` |  |
| Views/ConditionEditorWindow.axaml | 226 | `#ccc` | `Palette.Neutral.800` |  |
| Views/ConditionEditorWindow.axaml | 232 | `#2c3e50` | `Palette.Slate.700` |  |
| Views/ConditionEditorWindow.axaml | 232 | `#ccc` | `Palette.Neutral.800` |  |
| Views/ConditionEditorWindow.axaml | 242 | `#252525` | `Palette.Neutral.145` |  |
| Views/ConflictResolutionDialog.axaml | 9 | `#252525` | `Palette.Neutral.145` |  |
| Views/ConflictResolutionDialog.axaml | 14 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/ConflictResolutionDialog.axaml | 17 | `#1a1a1a` | `Palette.Neutral.100` |  |
| Views/ConflictResolutionDialog.axaml | 21 | `#888` | `Palette.Neutral.535` |  |
| Views/ConflictResolutionDialog.axaml | 23 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/ConflictResolutionDialog.axaml | 27 | `#888` | `Palette.Neutral.535` |  |
| Views/ConflictResolutionDialog.axaml | 29 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/ConflictResolutionDialog.axaml | 33 | `#888` | `Palette.Neutral.535` |  |
| Views/ConflictResolutionDialog.axaml | 35 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/ConflictResolutionDialog.axaml | 39 | `#888` | `Palette.Neutral.535` |  |
| Views/ConflictResolutionDialog.axaml | 41 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/ConflictResolutionDialog.axaml | 50 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/ConflictResolutionDialog.axaml | 55 | `#8e4912` | `Palette.Burnt.600` |  |
| Views/ConversationNameDialog.axaml | 8 | `#252525` | `Palette.Neutral.145` |  |
| Views/ConversationNameDialog.axaml | 13 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/ConversationNameDialog.axaml | 18 | `#141414` | `Palette.Neutral.80` |  |
| Views/ConversationNameDialog.axaml | 18 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/ConversationNameDialog.axaml | 19 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/ConversationNameDialog.axaml | 26 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/ConversationNameDialog.axaml | 30 | `#1a5276` | `Palette.Azure.600` |  |
| Views/ConversationView.axaml | 11 | `#aaa` | `Palette.Neutral.665` |  |
| Views/ConversationView.axaml | 15 | `#b8760a` | `Palette.Amber.500` |  |
| Views/ConversationView.axaml | 18 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/ConversationView.axaml | 26 | `#f0a830` | `Palette.Amber.520` |  |
| Views/ConversationView.axaml | 29 | `#bbb` | `Palette.Neutral.735` |  |
| Views/ConversationView.axaml | 36 | `#252525` | `Palette.Neutral.145` |  |
| Views/ConversationView.axaml | 41 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/ConversationView.axaml | 41 | `#aaa` | `Palette.Neutral.665` |  |
| Views/ConversationView.axaml | 45 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/ConversationView.axaml | 45 | `#aaa` | `Palette.Neutral.665` |  |
| Views/ConversationView.axaml | 49 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/ConversationView.axaml | 49 | `#aaa` | `Palette.Neutral.665` |  |
| Views/ConversationView.axaml | 53 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/ConversationView.axaml | 53 | `#aaa` | `Palette.Neutral.665` |  |
| Views/ConversationView.axaml | 58 | `#888` | `Palette.Neutral.535` |  |
| Views/ConversationView.axaml | 63 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/ConversationView.axaml | 63 | `#ccc` | `Palette.Neutral.800` |  |
| Views/ConversationView.axaml | 63 | `#ccc` | `Palette.Neutral.800` |  |
| Views/ConversationView.axaml | 64 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/ConversationView.axaml | 70 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/ConversationView.axaml | 77 | `#666` | `Palette.Neutral.400` |  |
| Views/ConversationView.axaml | 86 | `#7a6a8e` | `Palette.Mauve.500` |  |
| Views/ConversationView.axaml | 103 | `#aaaaaa` | `Palette.Neutral.665` |  |
| Views/ConversationView.axaml | 192 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/ConversationView.axaml | 203 | `#666` | `Palette.Neutral.400` |  |
| Views/ConversationView.axaml | 239 | `#1a1a2a` | `Palette.Navy.100` |  |
| Views/ExportConversationsWindow.axaml | 8 | `#1e1e1e` | `Palette.Neutral.115` |  |
| Views/ExportConversationsWindow.axaml | 24 | `#4a9eff` | `Palette.Sky.400` |  |
| Views/ExportConversationsWindow.axaml | 26 | `#888` | `Palette.Neutral.535` |  |
| Views/ExportConversationsWindow.axaml | 33 | `#4a9eff` | `Palette.Sky.400` |  |
| Views/ExportConversationsWindow.axaml | 36 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/ExportConversationsWindow.axaml | 43 | `#ddd` | `Palette.Neutral.865` |  |
| Views/ExportConversationsWindow.axaml | 62 | `#888` | `Palette.Neutral.535` |  |
| Views/ExportConversationsWindow.axaml | 81 | `#aaa` | `Palette.Neutral.665` |  |
| Views/DiffHelpWindow.axaml | 7 | `#1a1a2a` | `Palette.Navy.100` |  |
| Views/DiffHelpWindow.axaml | 10 | `#888` | `Palette.Neutral.535` |  |
| Views/DiffHelpWindow.axaml | 11 | `#7dcea0` | `Palette.Green.400` |  |
| Views/DiffHelpWindow.axaml | 11 | `#ccc` | `Palette.Neutral.800` |  |
| Views/DiffHelpWindow.axaml | 12 | `#f0ad4e` | `Palette.Amber.540` |  |
| Views/DiffHelpWindow.axaml | 12 | `#ccc` | `Palette.Neutral.800` |  |
| Views/DiffHelpWindow.axaml | 13 | `#e74c3c` | `Palette.Red.450` |  |
| Views/DiffHelpWindow.axaml | 13 | `#ccc` | `Palette.Neutral.800` |  |
| Views/DiffHelpWindow.axaml | 14 | `#888` | `Palette.Neutral.535` |  |
| Views/DiffHelpWindow.axaml | 15 | `#bbb` | `Palette.Neutral.735` |  |
| Views/DiffHelpWindow.axaml | 16 | `#888` | `Palette.Neutral.535` |  |
| Views/DiffHelpWindow.axaml | 17 | `#bbb` | `Palette.Neutral.735` |  |
| Views/DiffHelpWindow.axaml | 18 | `#888` | `Palette.Neutral.535` |  |
| Views/DiffHelpWindow.axaml | 19 | `#bbb` | `Palette.Neutral.735` |  |
| Views/DiffHelpWindow.axaml | 20 | `#888` | `Palette.Neutral.535` |  |
| Views/DiffHelpWindow.axaml | 21 | `#bbb` | `Palette.Neutral.735` |  |
| Views/DiffHelpWindow.axaml | 22 | `#888` | `Palette.Neutral.535` |  |
| Views/DiffHelpWindow.axaml | 23 | `#bbb` | `Palette.Neutral.735` |  |
| Views/DiffWindow.axaml | 11 | `#252525` | `Palette.Neutral.145` |  |
| Views/DiffWindow.axaml | 29 | `#aaa` | `Palette.Neutral.665` |  |
| Views/DiffWindow.axaml | 40 | `#ddd` | `Palette.Neutral.865` |  |
| Views/DiffWindow.axaml | 47 | `#aaa` | `Palette.Neutral.665` |  |
| Views/DiffWindow.axaml | 58 | `#ddd` | `Palette.Neutral.865` |  |
| Views/DiffWindow.axaml | 65 | `#1a1a1a` | `Palette.Neutral.100` |  |
| Views/DiffWindow.axaml | 67 | `#888` | `Palette.Neutral.535` |  |
| Views/DiffWindow.axaml | 73 | `#202020` | `Palette.Neutral.125` |  |
| Views/DiffWindow.axaml | 85 | `#ccc` | `Palette.Neutral.800` |  |
| Views/DiffWindow.axaml | 87 | `#888` | `Palette.Neutral.535` |  |
| Views/DiffWindow.axaml | 101 | `#e0a030` | `Palette.Amber.530` |  |
| Views/DiffWindow.axaml | 105 | `#888` | `Palette.Neutral.535` |  |
| Views/DiffWindow.axaml | 109 | `#ccc` | `Palette.Neutral.800` |  |
| Views/DiffWindow.axaml | 128 | `#888` | `Palette.Neutral.535` |  |
| Views/DiffWindow.axaml | 133 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/DiffWindow.axaml | 148 | `#ddd` | `Palette.Neutral.865` |  |
| Views/DiffWindow.axaml | 159 | `#ccc` | `Palette.Neutral.800` |  |
| Views/DiffWindow.axaml | 182 | `#7dcea0` | `Palette.Green.400` |  |
| Views/DiffWindow.axaml | 183 | `#aaa` | `Palette.Neutral.665` |  |
| Views/DiffWindow.axaml | 184 | `#f0ad4e` | `Palette.Amber.540` |  |
| Views/DiffWindow.axaml | 185 | `#aaa` | `Palette.Neutral.665` |  |
| Views/DiffWindow.axaml | 186 | `#e74c3c` | `Palette.Red.450` |  |
| Views/DiffWindow.axaml | 187 | `#aaa` | `Palette.Neutral.665` |  |
| Views/DiffWindow.axaml | 195 | `#1e1e1e` | `Palette.Neutral.115` |  |
| Views/DiffWindow.axaml | 196 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/DiffWindow.axaml | 202 | `#ddd` | `Palette.Neutral.865` |  |
| Views/DiffWindow.axaml | 205 | `#e0a030` | `Palette.Amber.530` |  |
| Views/DiffWindow.axaml | 214 | `#bbb` | `Palette.Neutral.735` |  |
| Views/DiffWindow.axaml | 216 | `#666` | `Palette.Neutral.400` |  |
| Views/DiffWindow.axaml | 221 | `#888` | `Palette.Neutral.535` |  |
| Views/DiffWindow.axaml | 224 | `#888` | `Palette.Neutral.535` |  |
| Views/DiffWindow.axaml | 227 | `#ddd` | `Palette.Neutral.865` |  |
| Views/DiffWindow.axaml | 231 | `#888` | `Palette.Neutral.535` |  |
| Views/DiffWindow.axaml | 234 | `#ddd` | `Palette.Neutral.865` |  |
| Views/DiffWindow.axaml | 239 | `#888` | `Palette.Neutral.535` |  |
| Views/DiffWindow.axaml | 242 | `#888` | `Palette.Neutral.535` |  |
| Views/DiffWindow.axaml | 245 | `#ddd` | `Palette.Neutral.865` |  |
| Views/DiffWindow.axaml | 249 | `#888` | `Palette.Neutral.535` |  |
| Views/DiffWindow.axaml | 252 | `#ddd` | `Palette.Neutral.865` |  |
| Views/DiffWindow.axaml | 271 | `#1e1e1e` | `Palette.Neutral.115` |  |
| Views/DiffWindow.axaml | 275 | `#ccc` | `Palette.Neutral.800` |  |
| Views/DiffWindow.axaml | 281 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/DiffWindow.axaml | 289 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/FindReplaceWindow.axaml | 9 | `#1e1e1e` | `Palette.Neutral.115` |  |
| Views/FindReplaceWindow.axaml | 15 | `#141414` | `Palette.Neutral.80` |  |
| Views/FindReplaceWindow.axaml | 16 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/FindReplaceWindow.axaml | 17 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/FindReplaceWindow.axaml | 23 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/FindReplaceWindow.axaml | 24 | `#ccc` | `Palette.Neutral.800` |  |
| Views/FindReplaceWindow.axaml | 31 | `#1a5276` | `Palette.Azure.600` |  |
| Views/FindReplaceWindow.axaml | 45 | `#aaa` | `Palette.Neutral.665` |  |
| Views/FindReplaceWindow.axaml | 55 | `#aaa` | `Palette.Neutral.665` |  |
| Views/FindReplaceWindow.axaml | 65 | `#ccc` | `Palette.Neutral.800` |  |
| Views/FindReplaceWindow.axaml | 68 | `#888` | `Palette.Neutral.535` |  |
| Views/ForceDeleteDialog.axaml | 9 | `#252525` | `Palette.Neutral.145` |  |
| Views/ForceDeleteDialog.axaml | 12 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/ForceDeleteDialog.axaml | 17 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/ForceDeleteDialog.axaml | 24 | `#7b241c` | `Palette.Crimson.700` |  |
| Views/GameBrowserView.axaml | 9 | `#252525` | `Palette.Neutral.145` |  |
| Views/GameBrowserView.axaml | 9 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/GameBrowserView.axaml | 13 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/GameBrowserView.axaml | 13 | `#ccc` | `Palette.Neutral.800` |  |
| Views/GameBrowserView.axaml | 13 | `#ccc` | `Palette.Neutral.800` |  |
| Views/GameBrowserView.axaml | 14 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/GameBrowserView.axaml | 20 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/GameBrowserView.axaml | 27 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/GameBrowserView.axaml | 33 | `#2d2d2d` | `Palette.Neutral.175` |  |
| Views/GameBrowserView.axaml | 42 | `#1a5276` | `Palette.Azure.600` |  |
| Views/GameBrowserView.axaml | 45 | `#1a5276` | `Palette.Azure.600` |  |
| Views/GameBrowserView.axaml | 51 | `#888` | `Palette.Neutral.535` |  |
| Views/GameBrowserView.axaml | 71 | `#1a1a1a` | `Palette.Neutral.100` |  |
| Views/GameBrowserView.axaml | 72 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/FlowAnalyticsWindow.axaml | 10 | `#1e1e1e` | `Palette.Neutral.115` |  |
| Views/FlowAnalyticsWindow.axaml | 16 | `#888` | `Palette.Neutral.535` |  |
| Views/FlowAnalyticsWindow.axaml | 20 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/FlowAnalyticsWindow.axaml | 25 | `#888` | `Palette.Neutral.535` |  |
| Views/FlowAnalyticsWindow.axaml | 31 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/FlowAnalyticsWindow.axaml | 32 | `#ccc` | `Palette.Neutral.800` |  |
| Views/FlowAnalyticsWindow.axaml | 95 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/FlowAnalyticsWindow.axaml | 105 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/FlowAnalyticsWindow.axaml | 123 | `#bbb` | `Palette.Neutral.735` |  |
| Views/FlowAnalyticsWindow.axaml | 129 | `#ccc` | `Palette.Neutral.800` |  |
| Views/FlowAnalyticsWindow.axaml | 137 | `#888` | `Palette.Neutral.535` |  |
| Views/FlowAnalyticsWindow.axaml | 151 | `#666` | `Palette.Neutral.400` |  |
| Views/GitConflictResolutionWindow.axaml | 8 | `#252525` | `Palette.Neutral.145` |  |
| Views/GitConflictResolutionWindow.axaml | 14 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/GitConflictResolutionWindow.axaml | 21 | `#888` | `Palette.Neutral.535` |  |
| Views/GitConflictResolutionWindow.axaml | 25 | `#1a1a1a` | `Palette.Neutral.100` |  |
| Views/GitConflictResolutionWindow.axaml | 30 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/GitConflictResolutionWindow.axaml | 32 | `#c08a2a` | `Palette.Amber.600` |  |
| Views/GitConflictResolutionWindow.axaml | 43 | `#1a2738` | `Palette.Navy.150` |  |
| Views/GitConflictResolutionWindow.axaml | 47 | `#9cc4ff` | `Palette.Sky.300` |  |
| Views/GitConflictResolutionWindow.axaml | 49 | `#888` | `Palette.Neutral.535` |  |
| Views/GitConflictResolutionWindow.axaml | 54 | `#888` | `Palette.Neutral.535` |  |
| Views/GitConflictResolutionWindow.axaml | 63 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/GitConflictResolutionWindow.axaml | 66 | `#3a1a1a` | `Palette.Maroon.900` |  |
| Views/GitConflictResolutionWindow.axaml | 70 | `#ff9c9c` | `Palette.Red.300` |  |
| Views/GitConflictResolutionWindow.axaml | 72 | `#888` | `Palette.Neutral.535` |  |
| Views/GitConflictResolutionWindow.axaml | 77 | `#888` | `Palette.Neutral.535` |  |
| Views/GitConflictResolutionWindow.axaml | 86 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/GitConflictResolutionWindow.axaml | 98 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/GitConflictResolutionWindow.axaml | 104 | `#2d6a2d` | `Palette.Green.600` |  |
| Views/ImportWarningsDialog.axaml | 9 | `#252525` | `Palette.Neutral.145` |  |
| Views/ImportWarningsDialog.axaml | 15 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/ImportWarningsDialog.axaml | 18 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/ImportWarningsDialog.axaml | 25 | `#ddd` | `Palette.Neutral.865` |  |
| Views/ImportWarningsDialog.axaml | 34 | `#888` | `Palette.Neutral.535` |  |
| Views/ImportWarningsDialog.axaml | 40 | `#1a5276` | `Palette.Azure.600` |  |
| Views/HistoryWindow.axaml | 8 | `#252525` | `Palette.Neutral.145` |  |
| Views/HistoryWindow.axaml | 14 | `#c08a2a` | `Palette.Amber.600` |  |
| Views/HistoryWindow.axaml | 21 | `#1a1a1a` | `Palette.Neutral.100` |  |
| Views/HistoryWindow.axaml | 29 | `#cfcfcf` | `Palette.Neutral.800` |  |
| Views/HistoryWindow.axaml | 31 | `#cfcfcf` | `Palette.Neutral.800` |  |
| Views/HistoryWindow.axaml | 33 | `#888` | `Palette.Neutral.535` |  |
| Views/HistoryWindow.axaml | 35 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/HistoryWindow.axaml | 47 | `#2d6a2d` | `Palette.Green.600` |  |
| Views/HistoryWindow.axaml | 51 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/LanguageCodeDialog.axaml | 9 | `#1e1e1e` | `Palette.Neutral.115` |  |
| Views/LanguageCodeDialog.axaml | 15 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/LanguageCodeDialog.axaml | 18 | `#141414` | `Palette.Neutral.80` |  |
| Views/LanguageCodeDialog.axaml | 19 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/LanguageCodeDialog.axaml | 20 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/LanguageCodeDialog.axaml | 30 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/LanguageCodeDialog.axaml | 30 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LanguageCodeDialog.axaml | 35 | `#1a5276` | `Palette.Azure.600` |  |
| Views/LegendWindow.axaml | 9 | `#1a1a2a` | `Palette.Navy.100` |  |
| Views/LegendWindow.axaml | 13 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 15 | `#aaa` | `Palette.Neutral.665` |  |
| Views/LegendWindow.axaml | 17 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 18 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 22 | `#b8760a` | `Palette.Amber.500` |  |
| Views/LegendWindow.axaml | 24 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 25 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 29 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/LegendWindow.axaml | 31 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 32 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 36 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 38 | `#F5F0D0` | `Palette.Parchment.100` |  |
| Views/LegendWindow.axaml | 39 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 42 | `#D5E8F5` | `Palette.Azure.150` |  |
| Views/LegendWindow.axaml | 44 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 45 | `#aaa` | `Palette.Neutral.665` |  |
| Views/LegendWindow.axaml | 49 | `#D5F0E8` | `Palette.Teal.150` |  |
| Views/LegendWindow.axaml | 50 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 53 | `#E0E0E0` | `Palette.Neutral.875` |  |
| Views/LegendWindow.axaml | 54 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 57 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 59 | `#e8a020` | `Palette.Amber.550` |  |
| Views/LegendWindow.axaml | 60 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 63 | `#aaa` | `Palette.Neutral.665` |  |
| Views/LegendWindow.axaml | 64 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 67 | `#aaa` | `Palette.Neutral.665` |  |
| Views/LegendWindow.axaml | 68 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 71 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 73 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 74 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 77 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 78 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 81 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 82 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 85 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 86 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 89 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 90 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 93 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 95 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 96 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 99 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 100 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 103 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 104 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 107 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 108 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 111 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 113 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 114 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 117 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 118 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 121 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 122 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 125 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 126 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 129 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 130 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 133 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 134 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 137 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 138 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 141 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 142 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 145 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 146 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 149 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 150 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 153 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 154 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 157 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 158 | `#888` | `Palette.Neutral.535` |  |
| Views/LegendWindow.axaml | 161 | `#ccc` | `Palette.Neutral.800` |  |
| Views/LegendWindow.axaml | 162 | `#888` | `Palette.Neutral.535` |  |
| Views/MainWindow.axaml | 8 | `#1e1e1e` | `Palette.Neutral.115` |  |
| Views/MainWindow.axaml | 13 | `#252525` | `Palette.Neutral.145` |  |
| Views/MainWindow.axaml | 35 | `#ccc` | `Palette.Neutral.800` |  |
| Views/MainWindow.axaml | 151 | `#888` | `Palette.Neutral.535` |  |
| Views/MainWindow.axaml | 186 | `#A0000000` | `Palette.Black @ 0xA0` |  |
| Views/MainWindow.axaml | 191 | `#252525` | `Palette.Neutral.145` |  |
| Views/MainWindow.axaml | 196 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/MainWindow.axaml | 196 | `#aaa` | `Palette.Neutral.665` |  |
| Views/MainWindow.axaml | 199 | `#888` | `Palette.Neutral.535` |  |
| Views/MainWindow.axaml | 203 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/MainWindow.axaml | 207 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/MainWindow.axaml | 211 | `#7dcea0` | `Palette.Green.400` |  |
| Views/MainWindow.axaml | 227 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/MainWindow.axaml | 227 | `#aaa` | `Palette.Neutral.665` |  |
| Views/MainWindow.axaml | 239 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/MainWindow.axaml | 245 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/MainWindow.axaml | 247 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/MainWindow.axaml | 253 | `#252525` | `Palette.Neutral.145` |  |
| Views/MainWindow.axaml | 258 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/MainWindow.axaml | 258 | `#aaa` | `Palette.Neutral.665` |  |
| Views/MainWindow.axaml | 261 | `#888` | `Palette.Neutral.535` |  |
| Views/MainWindow.axaml | 263 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/MainWindow.axaml | 278 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/MainWindow.axaml | 278 | `#aaa` | `Palette.Neutral.665` |  |
| Views/MainWindow.axaml | 286 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/MainWindow.axaml | 295 | `#1a1a1a` | `Palette.Neutral.100` |  |
| Views/MainWindow.axaml | 298 | `#888` | `Palette.Neutral.535` |  |
| Views/MainWindow.axaml | 304 | `#8e4912` | `Palette.Burnt.600` |  |
| Views/NodeDetailView.axaml | 11 | `#888` | `Palette.Neutral.535` |  |
| Views/NodeDetailView.axaml | 17 | `#999` | `Palette.Neutral.600` |  |
| Views/NodeDetailView.axaml | 22 | `#1a1a1a` | `Palette.Neutral.100` |  |
| Views/NodeDetailView.axaml | 23 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/NodeDetailView.axaml | 24 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/NodeDetailView.axaml | 31 | `#1a1a1a` | `Palette.Neutral.100` |  |
| Views/NodeDetailView.axaml | 32 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/NodeDetailView.axaml | 33 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/NodeDetailView.axaml | 40 | `#ccc` | `Palette.Neutral.800` |  |
| Views/NodeDetailView.axaml | 50 | `#999` | `Palette.Neutral.600` |  |
| Views/NodeDetailView.axaml | 60 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/NodeDetailView.axaml | 64 | `#2d2d2d` | `Palette.Neutral.175` |  |
| Views/NodeDetailView.axaml | 72 | `#bdbd80` | `Palette.Olive.500` |  |
| Views/NodeDetailView.axaml | 142 | `#2A2000` | `Palette.Amber.950` |  |
| Views/NodeDetailView.axaml | 142 | `#7A5C00` | `Palette.Amber.900` |  |
| Views/NodeDetailView.axaml | 149 | `#E8C050` | `Palette.Amber.560` |  |
| Views/NodeDetailView.axaml | 205 | `#888` | `Palette.Neutral.535` |  |
| Views/NodeDetailView.axaml | 211 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/NodeDetailView.axaml | 211 | `#aaa` | `Palette.Neutral.665` |  |
| Views/NodeDetailView.axaml | 235 | `#888` | `Palette.Neutral.535` |  |
| Views/NodeDetailView.axaml | 242 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/NodeDetailView.axaml | 242 | `#aaa` | `Palette.Neutral.665` |  |
| Views/NodeDetailView.axaml | 257 | `#5dade2` | `Palette.Sky.450` |  |
| Views/NodeDetailView.axaml | 261 | `#5dade2` | `Palette.Sky.450` |  |
| Views/NodeDetailView.axaml | 280 | `#888` | `Palette.Neutral.535` |  |
| Views/NodeDetailView.axaml | 288 | `#1a1a1a` | `Palette.Neutral.100` |  |
| Views/NodeDetailView.axaml | 288 | `#ccc` | `Palette.Neutral.800` |  |
| Views/NodeDetailView.axaml | 289 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/NodeDetailView.axaml | 301 | `#666` | `Palette.Neutral.400` |  |
| Views/NodeDetailView.axaml | 307 | `#1a1a1a` | `Palette.Neutral.100` |  |
| Views/NodeDetailView.axaml | 307 | `#ccc` | `Palette.Neutral.800` |  |
| Views/NodeDetailView.axaml | 308 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/NodeDetailView.axaml | 324 | `#1a1a1a` | `Palette.Neutral.100` |  |
| Views/NodeDetailView.axaml | 324 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/NodeDetailView.axaml | 325 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/NodeDetailView.axaml | 331 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/NodeDetailView.axaml | 331 | `#aaa` | `Palette.Neutral.665` |  |
| Views/SettingsWindow.axaml | 8 | `#1e1e1e` | `Palette.Neutral.115` |  |
| Views/SettingsWindow.axaml | 14 | `#aaa` | `Palette.Neutral.665` |  |
| Views/SettingsWindow.axaml | 20 | `#1a1a1a` | `Palette.Neutral.100` |  |
| Views/SettingsWindow.axaml | 21 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/SettingsWindow.axaml | 22 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/SettingsWindow.axaml | 29 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/SettingsWindow.axaml | 30 | `#ccc` | `Palette.Neutral.800` |  |
| Views/SettingsWindow.axaml | 73 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/SettingsWindow.axaml | 73 | `#ccc` | `Palette.Neutral.800` |  |
| Views/ScriptEditorWindow.axaml | 10 | `#1e1e1e` | `Palette.Neutral.115` |  |
| Views/ScriptEditorWindow.axaml | 15 | `#888` | `Palette.Neutral.535` |  |
| Views/ScriptEditorWindow.axaml | 21 | `#888` | `Palette.Neutral.535` |  |
| Views/ScriptEditorWindow.axaml | 27 | `#141414` | `Palette.Neutral.80` |  |
| Views/ScriptEditorWindow.axaml | 28 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/ScriptEditorWindow.axaml | 29 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/ScriptEditorWindow.axaml | 36 | `#141414` | `Palette.Neutral.80` |  |
| Views/ScriptEditorWindow.axaml | 37 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/ScriptEditorWindow.axaml | 38 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/ScriptEditorWindow.axaml | 45 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/ScriptEditorWindow.axaml | 46 | `#ccc` | `Palette.Neutral.800` |  |
| Views/ScriptEditorWindow.axaml | 53 | `#1a5276` | `Palette.Azure.600` |  |
| Views/ScriptEditorWindow.axaml | 66 | `#252525` | `Palette.Neutral.145` |  |
| Views/ScriptEditorWindow.axaml | 72 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/ScriptEditorWindow.axaml | 80 | `#aaa` | `Palette.Neutral.665` |  |
| Views/ScriptEditorWindow.axaml | 87 | `#aaa` | `Palette.Neutral.665` |  |
| Views/ScriptEditorWindow.axaml | 94 | `#c0392b` | `Palette.Red.500` |  |
| Views/ScriptEditorWindow.axaml | 141 | `#141414` | `Palette.Neutral.80` |  |
| Views/ScriptEditorWindow.axaml | 141 | `#ccc` | `Palette.Neutral.800` |  |
| Views/ScriptEditorWindow.axaml | 141 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/ScriptEditorWindow.axaml | 146 | `#2c3e50` | `Palette.Slate.700` |  |
| Views/ScriptEditorWindow.axaml | 146 | `#ccc` | `Palette.Neutral.800` |  |
| Views/ScriptEditorWindow.axaml | 163 | `#141414` | `Palette.Neutral.80` |  |
| Views/ScriptEditorWindow.axaml | 163 | `#ccc` | `Palette.Neutral.800` |  |
| Views/ScriptEditorWindow.axaml | 163 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/ScriptEditorWindow.axaml | 168 | `#2c3e50` | `Palette.Slate.700` |  |
| Views/ScriptEditorWindow.axaml | 168 | `#ccc` | `Palette.Neutral.800` |  |
| Views/ScriptEditorWindow.axaml | 185 | `#141414` | `Palette.Neutral.80` |  |
| Views/ScriptEditorWindow.axaml | 185 | `#ccc` | `Palette.Neutral.800` |  |
| Views/ScriptEditorWindow.axaml | 185 | `#444` | `Palette.Neutral.265` | [attr] |
| Views/ScriptEditorWindow.axaml | 190 | `#2c3e50` | `Palette.Slate.700` |  |
| Views/ScriptEditorWindow.axaml | 190 | `#ccc` | `Palette.Neutral.800` |  |
| Views/ScriptEditorWindow.axaml | 199 | `#252525` | `Palette.Neutral.145` |  |
| Views/UnsavedChangesDialog.axaml | 9 | `#252525` | `Palette.Neutral.145` |  |
| Views/UnsavedChangesDialog.axaml | 14 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/UnsavedChangesDialog.axaml | 20 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/UnsavedChangesDialog.axaml | 24 | `#333` | `Palette.Neutral.200` | [attr] |
| Views/UnsavedChangesDialog.axaml | 28 | `#1a5276` | `Palette.Azure.600` |  |
| Views/TestModeOverlay.axaml | 8 | `#BB000000` | `Palette.Black @ 0xBB` |  |
| Views/TestModeOverlay.axaml | 11 | `#252525` | `Palette.Neutral.145` |  |
| Views/TestModeOverlay.axaml | 12 | `#555` | `Palette.Neutral.335` | [attr] |
| Views/TestModeOverlay.axaml | 25 | `#f0a830` | `Palette.Amber.520` |  |
| Views/TestModeOverlay.axaml | 28 | `#e8e8e8` | `Palette.Neutral.910` |  |
| Views/TestModeOverlay.axaml | 37 | `#c0392b` | `Palette.Red.500` |  |
| Views/PatchManagerWindow.axaml | 10 | `#1e1e1e` | `Palette.Neutral.115` |  |


## Appendix B — converter constant inventory

| File | Constant(s) | Target token(s) |
|---|---|---|
| `SpeakerCategoryToBrushConverter` | 12 RGB (header/body/footer × 4) | `Brush.Node.{Npc,Player,Narrator,Script}.{Header,Body,Footer}` |
| `NodeColorConverter` | same 12 (duplicate) + 3 bark | same `Brush.Node.*` incl. `Brush.Node.Bark.*` — **delete duplicates** |
| `DiffStatusToBrushConverter` | `#3a7a3a` `#c08a2a` `#7a2a2a` | `Brush.Diff.{Added,Changed,Removed}.Fill` |
| `FlowIssueKindToSeverityBrushConverter` | `#c0392b` `#b8760a` | `Brush.Severity.{Error,Warning}` |
| `BoolToNewConversationBrushConverter` | `#7dcea0` `#cccccc` | `Brush.Text.Status.New`, `Brush.Text.Secondary` |
| `BoolToFemaleTextBrushConverter` | `#e8e8e8` `#555555` | `Brush.Text.Female.Active`, `Brush.Text.Female.Dim` |
| `PropertyValueStyleToBrushConverter` | `#e8a020` `#7dcea0` `#9cdcfe` `#e8e8e8` | `Brush.Syntax.{Condition,Script,Code,Default}` |
| `InlineDiffTextBlock` | (diff inline highlight RGB) | `Brush.Diff.*` / `Brush.Text.Status.*` — confirm at impl |
| `GitConflictResolutionWindow.axaml.cs` | mine/theirs RGB | `Brush.Conflict.{Mine,Theirs}.{Background,Foreground}` |

