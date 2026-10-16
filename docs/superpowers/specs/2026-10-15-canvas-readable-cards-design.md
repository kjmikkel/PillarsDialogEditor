# Readable canvas cards (issue 82, option B)

## Problem

The conversation canvas was never designed as a whole. The #82 screenshot review found:

- Node text is 10 px, the footer 9 px. At 100% zoom a writer can't comfortably read a line
  without opening the detail panel.
- The body is limited twice: `NodeViewModel.TextPreview` cuts at 80 characters, and the
  template clips at `MaxHeight="70"`. The clip leaves no "…", so text just stops.
- Font scale works against the card. The card stays 200 px wide and 70 px tall while the text
  grows, so **2.0× shows less of each line than 1.0×** (at 2.0× a 150-character line shows
  62 of its 130 px), and cards overlap.
- `AutoLayoutService` assumes 220 × 110 cards with gaps of 200 (columns) and 20 (rows). The
  cards are neither, so rows crowd while columns sit far apart.
- The ports are large dots in dark side columns that take a third of the node's width.
  Connections cross card text. The canvas is the same mid-grey (`#838585`) in every theme.
  The minimap covers content after fit-to-screen.

Options A, B and C were drawn up in
`docs/superpowers/mockups/2026-10-15-canvas-look-and-feel.html`. **B was chosen.** C
(level of detail by zoom) is #221 and builds on this design. The review's other findings are
out of scope here (see the end).

## Decision

Cards a writer can read at 100%: 12 px text, 240 px wide, growing to fit their line up to four
lines, placed by their real height. Two rules make that hold at every font scale and keep
existing projects intact:

1. **On the canvas, font scale magnifies the canvas instead of resizing text inside fixed
   cards.** The canvas's text, cards and gaps are defined in canvas units that the font scale
   never changes. Font scale multiplies the canvas zoom instead. At 2.0× everything on the
   canvas is drawn twice as large, so a card shows exactly the same text as at 1.0×. Its
   layout, its saved positions and its line breaks are unchanged.
2. **One source of truth for card size.** A `CardMetrics` class in `DialogEditor.Core` holds the
   card's width, font sizes, padding, line height and line cap. The XAML template reads the
   same values through tokens, and `AutoLayoutService` estimates card heights from them. A
   headless test checks that the estimate matches the real rendered card.

Considered and set aside:

- **Scaling the card width and height with the font scale**, as the mockup draws it. The
  result looks the same, but layout positions would have to change with the font scale.
  Saved layouts are stored in canvas units, and every save writes them, so a project would
  overlap or spread out whenever the setting changed. Magnifying the canvas gets the same
  picture with none of that.
- **Measuring real card heights after rendering, then laying out again.** It would be exact,
  but it needs a second layout pass after the view has drawn, which makes nodes jump on
  open. It also can't run in Core, where conversation import lays out nodes with no view at
  all (`MainWindowViewModel` import path). An estimate tied to a test is enough, because
  the row gap absorbs a small error.

## Design

### 1. Card metrics (`DialogEditor.Core/Layout/CardMetrics.cs`)

All values are in canvas units (px at 100% zoom, font scale 1.0):

| Value | |
|---|---|
| Card width | 240 |
| Header font | 12, bold, one line, trimmed with "…" |
| Body font | 12, line height 16 |
| Footer font | 10, one line |
| Body lines | 1 to 4; text past four lines ends in "…" |
| Padding | header 3 × 6, body 4 × 6, footer 2 × 6 |
| Header / footer height | derived: font line height + padding |
| Column gap / row gap | 100 / 28 |

`CardMetrics.EstimateHeight(string text)` returns header + body + footer height, with the
body's line count estimated from the text length and an average character width for the
body font. An empty text counts as one line, as the empty-text placeholder (#84) does. The
character width is a named constant, calibrated by the headless test in §7 to err on the tall
side: an estimate that is too tall only adds space, while one that is too short can overlap.

A four-line card is about 22 + 72 + 18 = **112** high. That is under the old auto-layout's
row pitch of 130 (110 + 20), and the new 240 width fits easily inside the old column pitch of
420. So **layouts saved before this change don't overlap** with the new cards, unless a
writer dragged nodes closer by hand. §5 states this as an invariant with a test.

### 2. The card template (`ConversationView.axaml`)

- **Font sizes and width:** the header, body and footer read new tokens
  `Canvas.FontSize.Header` / `.Body` / `.Footer` and `Canvas.CardWidth`, declared in
  `Tokens.axaml` with `CardMetrics`' values. They are deliberately **not** in
  `FontSizeTokens.BaseValues`, so `FontScaleApplier` leaves them alone (§3). A test pins the
  tokens to `CardMetrics`, as `ColumnWidthTokenTests` does for column widths.
- **Body text:** binds to the node's full text, with `TextWrapping="Wrap"`,
  `MaxLines="4"` and `TextTrimming="CharacterEllipsis"`, and no `MaxHeight`. The card's
  height comes from its content. `TextPreview` and its 80-character cut stay for the places
  that use it as a one-line summary: Node Details' link list, Batch VO Import, VO
  Validation and the VO alias picker. The placeholder text block gets the same `MaxLines`
  treatment.
- **Card edge:** a 1 px `Brush.Node.Border` outline, so light cards keep a visible edge
  on the light canvas (§6).
- **The overlay borders and badges** (search match, diff, connect-mode source) are untouched.
  They already span the card's rows and follow its size.

### 3. Font scale magnifies the canvas (`ConversationView.axaml.cs`)

The canvas zoom the user sees is `fontScale × userZoom`:

- **Zoom range:** `MinViewportZoom` and `MaxViewportZoom` are multiplied by the font scale,
  so the range of user zoom stays the same.
- **Opening a conversation:** the editor opens at zoom = font scale (100% user zoom), where it
  opens at 1.0 today.
- **Zoom in and out:** unchanged steps, multiplying the current zoom.
- **Fit to screen:** chooses its own zoom, as now. When a fitted conversation is too large to
  read, the user zooms in, the same as at 1.0×.
- **Live change:** when `FontScaleService.Current.Scale` changes, the editor multiplies its
  current zoom by `new / old` around the viewport centre, so an open conversation keeps its
  place and magnifies.
- **Annotations and the minimap** already follow `ViewportZoom` (`SyncScreen`), so they need
  no separate handling.

### 4. Ports

The Nodify connector columns go. Input and output are 8 px dots on the card's left and right
edges at header height, filled with `Brush.Connection.Default` and outlined in the canvas
background colour. They are drawn through restyled `NodeInput` / `NodeOutput` templates and a
`nodify:Node` control theme with no side columns or padding. Their hit area stays at least
16 × 16, so they are no harder to grab than today's ports, and the UIA names stay as they are.

### 5. Layout (`AutoLayoutService`)

- **Signature:** `Apply` takes the card height per node:
  `Apply(nodes, Func<int, double> heightOf, setLocation)`. Callers pass
  `id => CardMetrics.EstimateHeight(textOf(id))`. Both callers have the text at hand:
  `ConversationViewModel` has the string table, and the import path has `imported.Texts`.
- **Columns:** column pitch = card width + column gap (340).
- **Rows:** each node is placed by its own height. A node's desired Y is its parents'
  barycentre, as now. It is pushed down only as far as needed to sit row-gap (28) below the
  previous node's bottom edge, not by a fixed pitch.
- **Roots:** layer-0 roots are stacked the same way, centred on the origin.
- **Invariants (tested):**
  - no two auto-laid-out cards overlap, for any texts;
  - the vertical space between neighbours in a column is at least the row gap;
  - a four-line card is shorter than 130, the old row pitch.

### 6. Canvas, connections and minimap

- **Background:** `Brush.Canvas.Background` gets a value per palette instead of one shared
  grey:

  | Palette | Value | Why |
  |---|---|---|
  | Dark | `#2B2B2B` | a step lighter than panels (`#252525`), so it reads as its own surface |
  | Light | `#E4E4E0` | a step darker than panels (`#E9E9E7`) |
  | High Contrast | `#3A3A3A` | unchanged |
  | Colourblind | its base theme's value | |

  Each must keep resting connections (`Brush.Connection.Default`) at ≥ 3:1, and `Never`
  links visible. The existing palette contrast tests get these pairs.
- **Dot grid:** a faint dot grid (`Brush.Canvas.Grid`) that pans and zooms with the canvas,
  drawn as a tiled brush transformed by Nodify's viewport transform. It is optional
  polish: if it costs noticeable frame time on a large hub, drop it. The theme colour is the
  requirement.
- **Connections behind cards:** cards are opaque, and connections are drawn on the layer below
  the cards. The review saw links crossing card text. The cause is to be confirmed first
  (layer order, or a translucent card while search dims non-matches) and fixed at its
  cause, with a headless test that a card's body is drawn above a connection that passes
  under it.
- **Minimap:** fit-to-screen fits the content into the viewport minus the minimap's corner
  (its width and height plus margin), so no card ends up under it.

### 7. Testing

TDD throughout, red first.

- **`CardMetricsTests`:** heights for empty, one-line, four-line and longer texts. Longer
  texts cap at four lines, and the estimate never decreases as text grows.
- **`CanvasCardRenderTests`** (headless, Skia text measurement as in `NoClippedTextTests`):
  - renders the real node template for a set of sample lines (short, long, no spaces,
    female suffix, conditions);
  - the card's rendered height ≤ `EstimateHeight` + 4 and ≥ `EstimateHeight` − 16 (at most
    one line too tall);
  - its width = `CardMetrics.Width`;
  - text past four lines ends trimmed.

  This ties the template to the layout's numbers, as #82's done-when asks.
- **Font scale:** at 1.0 and 2.0, the same card shows the same lines:
  - identical line breaks and trimming;
  - the canvas font tokens are unchanged by `FontScaleApplier`;
  - the editor's zoom is 2× at font scale 2.0;
  - a live change from 1.0 to 2.0 doubles the zoom and keeps the viewport centre.
- **`AutoLayoutServiceTests`:** the §5 invariants, existing behaviour (barycentre,
  input-order tiebreak, roots centred, back-edges ignored) with variable heights, and both
  callers passing estimated heights.
- **Tokens:** `Canvas.*` tokens are pinned to `CardMetrics`. The new `Brush.Canvas.*` and
  `Brush.Node.Border` values exist in every palette and pass the contrast tests.
- **Existing suites stay green:** `NoClippedTextTests`, `UiaAddressability` and the GUI
  tests that find canvas controls by name.
- **The PR:** before/after screenshots from the `running-the-app` tooling, in Dark and Light
  at font scale 1.0 and 2.0, as opened and fitted, of a short, a long and a branching
  conversation. #82 asks for these.

### Comments in the files

As the project does elsewhere, the reasoning lives next to the code. `CardMetrics` explains
the single-source rule and why the estimate errs tall. The template explains why its canvas
tokens aren't font-scaled. `ConversationView.axaml.cs` explains that font scale magnifies the
canvas. `AutoLayoutService` explains the old-pitch invariant.

## Out of scope

- **#221:** level of detail by zoom (option C). It builds on this design.
- **#222:** every header showed the bark colour in the review. The tokens already differ, so
  that is a bug in how the colour is chosen, not part of this design.
- **Finding 9:** the menu bar, panel titles and canvas toolbar ignore font scale, and the
  clear buttons in the filter and search boxes clip at 1.5× and 2.0×. Those are the app's
  chrome, not the canvas. They are to be filed separately if they aren't already covered.
- **No colour changes** to node headers or bodies, and no new node types or states.

## Done when (#82)

- **Font scale:** 2.0 shows at least as much of a card's text as 1.0. With this design it
  shows exactly the same, which the font-scale tests check.
- **Layout:** auto-layout spacing comes from the rendered card size, through `CardMetrics`
  and `CanvasCardRenderTests`.
- **Screenshots:** the PR includes the before/after set listed in §7.
- **Comment:** the decisions above are posted on #82 with a link to this spec.
