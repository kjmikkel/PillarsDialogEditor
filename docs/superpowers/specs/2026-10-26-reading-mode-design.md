# Reading mode — design

GitHub issue: #234. Audience definitions: [`docs/audience.md`](../../audience.md).

## Why

The editor has two primary audiences. **Readers** open the games' conversations to read
them (lore, companion banter, branches they missed) and never edit. **Modders** spend hours
building and shipping conversations. Today both land on the same screen, full of editing
tools a Reader doesn't understand and is afraid to touch. `audience.md` names a separate
reading mode as the long-term answer, and until it exists the Modder wins on every
editing screen.

Reading mode gives Readers screens built for them without taking anything away from
Modders, and the ladder in `audience.md` (Reader → Curious/Fixer → Modder) becomes one
deliberate switch instead of a wall.

## Goals

- A Reader can open a game folder, find a conversation, read it and follow its branches
  without seeing editing controls.
- **In reading mode nothing can change a game file, a project file or settings beyond
  view preferences.** This is the hard requirement; everything else serves it.
- Reading needs no knowledge of projects, diffs or Git.
- Switching between reading and editing is obvious, always available and deliberate.
- Modders who pick editing see the app they have today, plus the mode switch and badge.

## Non-goals

- A script-style or text-only reading view. The **node graph on the canvas stays the
  reading surface** (decided).
- Showing the untouched game text while a project is open. With a project open, reading
  mode shows the project's version. To read the original, close the project. A dedicated
  "show original game text" option may come later as its own issue.
- A separate reading-mode layout file or theme.

## Anti-goals (would make this a failure even if it works)

- Any route from reading mode to a write: a shortcut, a context-menu item, a dialog
  reachable from a kept window, a prompt that fires on open.
- Hiding an editing action only by greying it out while its shortcut still works.
- Showing the mode by colour alone.
- A Modder losing unsaved work because of a mode switch.

## Decided

### 1. Where reading mode lives

A mode inside the main window (from the issue): **View › Reading mode** is a checkable
toggle. It hides editing menus and controls and makes everything read-only. Conversations,
the canvas and Node Details stay.

### 2. First-launch mode chooser

- A small modal window, **"How do you want to use the editor?"**, shown **once, on the
  very first launch, for everyone**. There are no existing users to migrate.
- **Layout: two panes side by side** (Reading on the left, Editing on the right). Each pane
  has a title with an icon, a sentence on who it is for, three bullets and its own button
  ("Start reading" / "Start editing"). Mockup: the "Side by side" variant in
  [`2026-10-26-reading-mode-mockups/mode-chooser.html`](2026-10-26-reading-mode-mockups/mode-chooser.html). Wording below is a starting point, not final copy.
  - Reading: "For reading the games' conversations: lore, companion banter, branches you
    missed." Bullets: open a game folder and find a conversation; read it and follow its
    branches; nothing can be changed, so you can't break your game.
  - Editing: "For fixing lines and making mods: every editing tool, projects and
    packaging." Bullets: edit text, conditions, scripts and links; write new
    conversations in a project; package your changes as a mod.
- A footer line under both panes: the window only appears once, closing it starts Reading
  mode, and modes can be switched later from View › Reading mode.
- **Picking a mode saves it.** Every later launch opens in the saved mode.
- **Closing the window without picking starts Reading mode, and saves that.**

### 3. What reading mode shows and hides

Read-only throughout. "Hide" means the item is not in the menu at all and its shortcut does
nothing. It is not just disabled.

**Menus**

| Menu | Kept in reading mode | Hidden |
|---|---|---|
| File | Open Game Folder…, **Open Project…**, **Recent Projects**, **Close Project** (only while a project is open, as today), Settings… | New Project, Save, Save As, Merge Projects, Patcher, Import/Export Conversation, Export for Translation, Import Translation, Export Mod Bundle |
| Edit | Browse Speaker Lines…, **Reputation & Disposition Balance…** | Undo, Redo, Find / Replace, Batch Find / Replace, Find in Project, Flow Analytics, Compare Versions, History, Attribution, Branches |
| View | Conversations, Node Details, **Condition search**, Show Minimap, Hide private paths, Reset Layout, **Reading mode** (new toggle) | — |
| Test | — | the whole menu (Test Patch, End Test and Restore, Restore Backup, Validate Voice-Over, Batch import VO ×2, Validate Text) |
| Help | Start Guided Tour (starts the Reader tour, see §6), Changelog…, About… | Create Sample Project, Open Walkthrough, Text Tag Reference, Export/Import UI strings |

The project items are a deliberate asymmetry: **Open Project, Recent Projects and Close
Project stay, New Project does not.** Opening lets people read fan mods. Closing is how
someone reading a project gets back to the plain game files, and it is always safe
because reading mode never has unsaved changes. Don't "tidy" these by hiding all project
items.

**Toolbar and main window chrome:** language picker, ⚙ Settings and ? help stay. On the
welcome (nothing-open) screen, Open Game Folder stays. The editing Guided Tour button there
is replaced by one that starts the Reader tour. The "resolve conflicts" banner is hidden.

**Conversations pane:** unchanged. The filter, expand/collapse, the "only project
conversations" checkbox and the "edited in project" markers all stay. The last two matter
when someone is reading their own or a fan's project.

**Canvas:** zoom, fit, jump to start, node search and the minimap stay. "Choose start
node" and "Add note" buttons are hidden. The node right-click menu keeps only "Show all
lines by this speaker". The empty-space right-click menu (add node, add note) is not shown.
Double-click on empty space does not add a node. Nodes cannot be dragged into new positions.

**Node Details:** read-only. It shows the default and female text, the speaker, the node
type and the links as a followable list. Conditions, scripts and developer comments show
as read-only text for the Curious. Hidden: add/delete link, the Edit Conditions and Edit
Scripts buttons, the dictionary buttons, spelling and text-tag warnings, and the VO
alias/reuse controls.

**Condition search pane:** available (it never writes), closed by default in reading mode.

**Browse Speaker Lines and Reputation & Disposition Balance without a project (in scope,
decided):** both require an open project today (`CanBrowseSpeakerLines`,
`CanShowRepDispositionBalance`), and the balance window's view model takes a
`DialogProject` in its constructor. As part of this issue, **both must work with only a
game folder open**, reading the conversations on disk. This is real refactoring, not a
flag flip. With no project, the balance window's source picker offers only "on disk".
With a project open, it offers the existing choices. Both run without a project in
editing mode too, since neither writes anything.

**Keyboard:** canvas navigation (arrows, Page Up/Down, Home, Enter, Esc), F6 pane
cycling, Ctrl+Shift+O (open game folder), Ctrl+O (open project), Ctrl+W (close project)
and Ctrl+, (settings) work. Every other main-window and canvas shortcut does nothing in
reading mode, including Ctrl+N, Ctrl+S, Ctrl+Shift+S, Ctrl+Z/Y, Delete, F5, Shift+F5, F7,
Ctrl+Shift+B, Ctrl+F, Ctrl+H, Ctrl+Shift+H, Ctrl+Shift+F, Ctrl+L and Ctrl+arrow nudging.

### 4. Opening things in reading mode

- With no project, reading mode shows the game files.
- With a project open, it shows **the project's version** of each conversation, read-only.
- Opening a project in reading mode must not show prompts that would write: the autosave
  restore offer and the Git conflict resolution flow wait until the user switches to
  editing. *(My call, not discussed in detail.)*
- **Policy for every other open-time side effect:** these two prompts are the known
  cases, not the full list. The plan must include an audit of everything that happens on
  open (format migration or upgrade, lock or temp files, Git operations, the autosave
  timer, recent-projects bookkeeping). Any write to the project folder or the game folder
  is gated behind editing mode. Writes to the app's own settings (for example the
  recent-projects list) are allowed, because they are the app's state, not the user's
  work. *(My call.)*
- **Same audit for kept windows:** every window reachable in reading mode (Browse Speaker
  Lines, Reputation & Disposition Balance, Settings, Changelog, About, and the
  Conversations, Node Details and Condition search panes) is checked for actions that write
  to game or project files, and any found are hidden in reading mode. The audit's findings
  become the list of "routes that matter" that the tests cover. Settings writes only the
  app's own settings (text size, autosave interval, backup folder, dictionaries), so it
  stays unchanged. *(My call.)*

### 5. Switching modes

- Three equivalent routes: **View › Reading mode**, the **mode badge** (§7), and the
  first-launch chooser.
- Every switch saves the new mode as the one to open in next time.
- **Editing → Reading with unsaved changes:** show the existing **Unsaved Changes** dialog
  (`UnsavedChangesDialog`: *Save to Project*, *Discard Changes*, *Stay Here*). Save or
  discard, then switch. *Stay Here* cancels the switch. **No "Don't ask me again"
  checkbox.** Locking in Discard would let one stray click lose work. No dialog when
  there are no unsaved changes.
- **The open project stays open.** After the switch the user is reading the same project,
  the same conversation and, where possible, the same selected node.
- **Reading → Editing:** no prompt. Any prompts deferred under §4 appear at this point.

### 6. Reader tour

- A second list of steps on the existing guided-tour machinery (`GuidedTourViewModel`,
  `TourTargetResolver`). It is not a second tour system.
- Steps (draft copy):
  1. Conversations: find a conversation by name or area and open it.
  2. Canvas: each box is a line. Follow the arrows, or use the arrow keys, to walk a branch.
  3. Node Details: the full line, who says it, and where it can lead next.
  4. The mode badge / View › Reading mode: where to switch to editing when you want to
     change something.
- **Starts on its own, like the editing tour does today (decided).** Each tour starts
  automatically the first time its mode is shown, and each has its own "seen" flag. Someone
  who picks Reading gets the Reader tour; if they later switch to Editing for the first
  time, the editing tour starts then. *(The per-mode "first time" rule is my call.)*
- It can be rerun from Help › Start Guided Tour, which starts the tour for the current
  mode. The chooser and the written docs don't mention it.

### 7. Mode badge

- A small rounded rectangle at the **right end of the toolbar**, after ⚙ and ?.
- **Reading:** green, 📖 icon, the word "Reading". **Editing:** amber, ✎ icon, the word
  "Editing". Mockup: [`2026-10-26-reading-mode-mockups/mode-badge.html`](2026-10-26-reading-mode-mockups/mode-badge.html).
- **Clicking it switches modes**, exactly like View › Reading mode, unsaved-changes dialog
  included.
- Accessibility: the mode is never shown by colour alone (its own icon and word). It is a
  focusable button with a UIA Name that includes the mode and what it means ("Reading mode:
  nothing can be changed"). Its tooltip explains the mode and that clicking switches. Its
  colours come from theme tokens in every theme, including high contrast, and meet
  contrast requirements.

## Constraints (from the repo)

- Strict red/green TDD (`CLAUDE.md`). Tests in `DialogEditor.Tests`.
- Every visible string goes in `Strings.axaml`. Every new control gets a detailed tooltip.
  Every new window gets the app icon and `WindowSizing`.
- Controls stay discoverable through UI Automation. A GUI check should be able to read the
  badge to know the mode.
- Caught exceptions are logged via `AppLog`.
- The existing read-only state (`ConversationViewModel.IsEditable`, which drives
  `NodeDetailViewModel.IsReadOnly` and the canvas commands' `CanExecute`) is what "read
  only" should mean. Reading mode should drive that flag instead of adding a second lock.
  Today it is set from "is a project open" (`MainWindowViewModel`: `Canvas.IsEditable =
  project is not null`). It becomes "a project is open **and** we are in editing mode".

## Left to the builder

- Where the saved mode lives (a new `AppSettings` value is the obvious place). "No saved
  mode" means "show the chooser".
- Sequencing with the existing first-launch flow: today a fresh install shows
  `ThemeOnboardingWindow`, then the main window, which auto-starts the editing tour
  (`GuidedTourSeen`). The chooser comes after theme onboarding, and the tour for the
  chosen mode starts once the main window is up (§6).
- How hiding is implemented: bindings on a single `IsReadingMode` property, styles or
  classes. It must be testable headless, with one test per hidden route that matters for
  the hard requirement.
- **Shortcuts (my call on the approach):** gate centrally. Every editing command's
  `CanExecute` includes "not reading mode", and the main-window and canvas key handlers
  check the mode once before dispatching, so a shortcut can't bypass a hidden menu item.
  Don't remove and re-add bindings per mode.
- **`IsEditable` as the single lock (my call):** the plan starts by verifying it really is
  the choke point. Look for Node Details fields that commit on focus loss, drag-to-move on
  the canvas, and any write path that doesn't check it, and route any found through it.
  An edit still pending in a text box when the user switches is committed first, so the
  Unsaved Changes dialog sees it.
- **Condition search closed by default (my call):** it applies to the default layout only.
  A saved layout is respected, so switching modes doesn't open or close panes.
- The chooser falling back to stacked panes when the window is too narrow at large text
  sizes.
- Exact colours, icons and final copy, within the rules above.
- Settings window contents in reading mode (my call: unchanged, since settings are view
  preferences and the app's own state, not game or project files).
