# Conversations dock, part A: visual pass (issue 85)

Issue #85 is split into four sub-projects, each with its own spec and PR:
**A, visual pass** (this one), B names and structure, C project awareness, and D prototype and
test content. The user approved the split and order, and asked for the work to continue through
A–D without check-ins unless something needs them. The decisions below are mine, made under that
instruction; each records why, so the user can overturn it in review.

## Already done elsewhere

- **Selection colours:** #83 (`Brush.Selection.*`, styled label colours).
- **Footer colour:** #180 (`Brush.Text.Hint` instead of the disabled colour).
- **Filter box and clear button:** use minimum, not fixed, sizes (#177).
- **The dock header ("Conversations") scales with the font:** #176 (`DockFontSizeNormal`). Captures
  at 2.0× confirm it, so A changes nothing there.

## Problems A fixes (#85 findings 1–4, 7)

1. **Row text is 10 px in a 32 px row.** At 1.0× each line sits in wide empty bands. At 2.0× the
   text fills the row.
2. **Folder names are the faintest text in the dock** (`Brush.Text.Muted`). With every folder
   collapsed, which is how the dock opens, folders are all you see.
3. **Long names run off the dock's edge with no ellipsis.** The tree scrolls sideways, so a label
   never gets a width to trim to.
4. **"(new)" appears twice.** A new conversation is listed as `my_conversation (new)` inside the
   `(new)` folder.
5. **The game-name footer is 9 px** (`FontSize.Caption`), the smallest text in the app.

## Decisions

| # | Decision | Why |
|---|---|---|
| A1 | Folder and conversation labels use `FontSize.Body`. | It's the app's reading size and follows the font scale. 10 px was chosen for density, but the 32 px rows wasted that density anyway. |
| A2 | Row height follows the text. Rows set `MinHeight="0"` (no Fluent 32 px floor), and labels get `Padding="2,4"`. | At 1.0× a row is about 24 px: denser than today, and still the WCAG 2.5.8 minimum target size. At 2.0× it grows with the text, so nothing clips. |
| A3 | Folder labels use `Brush.Text.Primary`, semi-bold; conversations stay `Brush.Text.Secondary` (new ones `Brush.Text.Status.New`). | Folders are the structure you scan first, so they get the strongest text, not the faintest. Weight, not only colour, separates the two kinds of row. |
| A4 | The tree no longer scrolls sideways; labels trim with an ellipsis and carry the full name as a tooltip. | The dock is narrow. A trimmed name with a tooltip beats a name cut off at the edge, or a horizontal scrollbar. |
| A5 | New conversations drop the " (new)" suffix. The `(new)` folder and the green colour already say it, and the folder is a cue that doesn't rely on colour. | Says it once. The unused `Label_NewConversation_Suffix` string is removed. |
| A6 | The footer stays and uses `FontSize.Small`. | It tells PoE1 from PoE2 at a glance, and that is worth a row. 9 px was below every other text in the app. |

Better folder names (`(root)`, `(new)`, raw paths) are part B, not A.

## Testing (TDD; each test fails first)

`GameBrowserLayoutTests` (headless, on the real `GameBrowserView`):

- Folder and conversation labels resolve `FontSize.Body`, and follow a 2.0× font scale.
- At 1.0× each row is at least 24 px and at most 30 px. At 2.0× no label is taller than its row.
- A long conversation name is trimmed (`TextTrimming` is `CharacterEllipsis`, and its desired width
  exceeds its arranged width), and its tooltip holds the full name.
- The tree's horizontal scrollbar is disabled.
- The folder label resting colour is `Brush.Text.Primary` (this updates
  `GameBrowserSelectionContrastTests.UnselectedRows_KeepTheirRestingColours`).

`PaletteContrastTests`: `Text.Primary`, `Text.Secondary` and `Text.Status.New` on `Surface.Subtle`
(the tree background), as normal text in every theme.

`GameBrowserViewModelTests`: a new conversation's `DisplayName` is its name, with no suffix.

## Verification

Full headless suite; dock captures (`Capture-ConversationsDock.ps1`), Dark/Light/HighContrast at
1.0× and 2.0×, before and after, attached to the PR.
