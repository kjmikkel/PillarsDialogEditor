# Conversations dock, part C: project awareness (issue 85)

Part C of the #85 split. The user asked for A–D to proceed without check-ins, so the decisions
below are mine; each says why, so it can be overturned in review.

## Problem

A writer can't see which conversations their project touches. In a game with hundreds of
conversations, a conversation the project has changed looks the same as one it hasn't. Only new
conversations are marked, by the New conversations folder and their green colour.

## What "changed by the project" means

The open `DialogProject` holds one `ConversationPatch` per conversation, keyed by conversation
name. A conversation counts as **edited** when its patch exists and isn't empty
(`ConversationPatch.IsEmpty` is false). An empty patch can be left by opening a conversation
without changing it, and that isn't an edit.

Patches reach the project when the editor commits the open conversation (on switching or saving).
Every change to the project goes through `MainWindowViewModel.SetProject`, which is where the dock
is told.

## Decisions

| # | Decision | Why |
|---|---|---|
| C1 | **An edited conversation shows a "●" at the right edge of its row**, in `Brush.Text.Status.Changed`. The dot has a tooltip, "Changed in this project", and the row's `AutomationProperties.ItemStatus` says the same. | The glyph is a cue that doesn't depend on colour. At the right edge, the dots form one column you can scan, and the name trims before them (a dot right after the name would stop the name from trimming). Its colour matches how the diff and history windows show "changed". The row's accessible name stays the conversation name, so automation that finds a row by name keeps working, and screen readers hear the status. |
| C2 | **A checkbox under the filter box: "Only this project's conversations".** It narrows the tree to edited and new conversations, combines with the text filter, and expands every folder it keeps. It shows only while a project is open, and turns off when the project closes. | A writer returning to their work shouldn't have to scroll past hundreds of untouched conversations. A labelled checkbox is easier to find than a glyph-only toggle (a project UI preference). |
| C3 | **Marks are refreshed on every `SetProject`.** The browser's `SetProjectConversations(edited)` sets each item's `IsEdited` flag; `null` means no project is open. | That one call covers every route a project changes by: open, close, commit, merge, apply from diff, prune, rename. Setting flags on about 2,000 items is cheap; rebuilding the tree on every commit is not, which is why `Load` stays tied to the new-conversation list. |
| C4 | **New conversations keep their own marking** (green, in New conversations). They can also be edited, and the dot shows then. | The two are independent facts. |

## Shape

- **`ConversationItemViewModel`** becomes an `ObservableObject` with an `IsEdited` property.
- **`GameBrowserViewModel`:**
  - `SetProjectConversations(IReadOnlySet<string>? edited)`;
  - `HasProject` (bool);
  - `OnlyProjectConversations` (bool, observable). Changing it refreshes `FilteredFolders` and `FilteredRoots`; it is reset when the project closes;
  - the recursive filter takes an item predicate: text match AND (not only-project OR edited OR new).
  - `IsFiltering` is true when there's filter text or the checkbox is on; folders kept by either are expanded.
- **`MainWindowViewModel.SetProject`:** after the possible `Browser.Load`, it calls
  `Browser.SetProjectConversations(project is null ? null : <names of non-empty patches>)`.
- **View:**
  - the CheckBox row under the filter, visible when `HasProject`, with a tooltip;
  - the conversation template becomes a `*,Auto` grid: the name (trimming) and the dot (visible when `IsEdited`);
  - `AutomationProperties.ItemStatus` on the container, bound to `IsEdited` through a converter.
- **Strings:** `Browser_OnlyProjectConversations` (label), `ToolTip_OnlyProjectConversations`,
  `Browser_EditedInProject` (the dot's tooltip and the item status).

## Testing (TDD)

- **`GameBrowserViewModelTests`:**
  - `SetProjectConversations` marks exactly the named items, including nested ones and top-level conversations, and clears marks on a later call;
  - `null` clears every mark, sets `HasProject` false and turns the checkbox off;
  - the only-project filter keeps edited and new conversations, combines with text, and expands their folders.
- **`MainWindowViewModel`:** opening a project with a non-empty and an empty patch marks only the first. Closing the project clears marks.
- **View (headless):**
  - the dot is visible only on an edited row, and its tooltip is set;
  - the checkbox is hidden with no project and shown with one;
  - the edited row's `ItemStatus` is set.
- **Contrast:** `Text.Status.Changed` on `Surface.Subtle` at the large-UI threshold, since the dot is a non-text indicator (3:1; 4.5:1 in High Contrast).

## Verification

Full headless suite. Dock captures with a scratch project holding a patch, once the desktop is
available.
