# Pillars Dialog Editor

A dialogue editing tool for *Pillars of Eternity* and *Pillars of Eternity II: Deadfire*.
Edits are stored as semantic diff files (`.dialogproject`) rather than modifying the
original game files directly, making the workflow safe, reversible, and shareable.

---

## Prerequisites

- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- A PoE1 or PoE2 installation

---

## Workflow

### 1 — Open your game folder

**File › Open Game Folder** (`Ctrl+Shift+O`)

Select the root directory of your PoE1 or PoE2 installation. The conversation
browser populates in the left panel. This choice is remembered between sessions.

**First-time setup:** The first time you open a game folder, the editor will ask
you to choose a backup destination. It then copies the original conversation and
string-table files to that location. This backup is the safety net for
**Test › Restore Backup** and does not need to be repeated unless you want to
update it.

---

### 2 — Open or create a project

**File › New Project** (`Ctrl+N`) or **File › Open Project** (`Ctrl+O`)

A project file (`.dialogproject`) is the home for all your changes. It stores your
edits as a compact JSON diff against the original game files — nothing in the game
directory is touched until you explicitly test or apply the project.

A project can contain patches for **any number of conversations**. Work on as many
as you like; they are all saved into the same file and applied together at test time.

> **Browsing works without a project open.** You can navigate and read any
> conversation freely. Editing requires an open project. The status bar will
> remind you when you are in read-only mode.

---

### 3 — Browse and edit

Click any conversation in the browser to open it on the canvas. To start a brand-new
conversation that doesn't exist yet in the game folder, click the **⊕** button in the
browser header — a name dialog opens, and the new entry appears in a **(new)** folder
shown in green. The conversation file is **not** written to disk until you press
`F5`; `F6` deletes it again, leaving the project entry intact for the next test cycle.

| Action | How |
|--------|-----|
| Select a node | Click it on the canvas |
| Add a node | Double-click the canvas background |
| Add a connected node | Right-click a node › Add connected node |
| Connect two nodes | Drag from a node's output port to another node's input port |
| Delete a node | Select it, press `Delete`; or right-click › Delete node |
| Delete a connection | Right-click the connection line › Delete connection |
| Search nodes | `Ctrl+F`, or type in the search box above the canvas |
| Find / Replace text | `Ctrl+H` — searches DefaultText and FemaleText across all nodes |
| Undo / Redo | `Ctrl+Z` / `Ctrl+Y` — all edits are fully undoable within a session |

#### Node detail panel

Selecting a node opens the detail panel on the right. Fields available for editing:

| Field | Description |
|-------|-------------|
| Default / Male text | The dialogue line shown to all players, or the male variant in gendered games |
| Female text | Optional female-voice override; leave blank to use the default text for both genders |
| Type | NPC Line or Player Choice |
| Speaker category | NPC, Player, Narrator, or Script — controls the node's colour on the canvas and the `xsi:type` / `$type` written to the game file |
| Speaker / Listener GUID | The characters involved; in PoE2 a name picker is available |
| Display type | Conversation (full portrait) or Bark (floating text) |
| Persistence | OnceEver hides this node after it has been shown once |
| Actor direction | Internal note for the voice actor |
| External VO | Voice-over file path or identifier |
| Comments | Internal developer notes — not shown to players |

#### Conditions

Each node can carry a list of conditions that the engine evaluates at runtime.
Click **Edit…** next to the CONDITIONS header to open the Condition Editor.

- Pick from a searchable catalogue of 166 known conditions (game-appropriate
  filtering when a game folder is loaded; parameters use enum dropdowns or
  Guid fields as appropriate for each game).
- Toggle **AND/OR** and **NOT** per condition, including on grouped branches.
- **Link conditions:** the **⚙** button on any link row opens the Condition
  Editor for that specific connection.
- **Grouped conditions:** existing groups show an **Edit group…** button that
  opens a nested Condition Editor; **+ New group** creates an empty nested group
  from scratch.
- Use ↑ ↓ and ✕ to reorder and remove; drag the editor to any monitor.

#### Scripts

Nodes can fire scripts at three lifecycle points. Click **Edit…** next to the
LOGIC header to open the Script Editor.

Three sections — **On Enter** (node shown), **On Exit** (player leaves),
**On Update** (every tick while active).

- Search the catalogue of 37 known scripts by typing, or press the down arrow
  to browse the full list. Selecting a known script pre-populates all parameters
  with named fields, correct types (enum dropdowns with game source values), and
  `AutoCompleteBox` filtering for large lists such as the 195-entry PoE1 or
  322-entry PoE2 area dropdowns on `AreaTransition` and `PreloadScene`.
- PoE1 and PoE2 variants of the same script (different parameter types) are
  shown separately and filtered to the loaded game.
- Unknown scripts can still be added by typing the C# reflection FullName
  directly, e.g. `Void SetGlobalValue(String, Int32)`.

---

### 4 — Save to project

**File › Save Project** (`Ctrl+S`)

Records the current conversation's changes as a diff inside the open project file.
The diff captures only what changed — text edits, node additions/removals, link
changes, condition and script modifications. Game files are not modified.
The project file is plain JSON and can be version-controlled or shared freely.

---

### 5 — Test in-game

**Test › Test Patch** (`F5`)

Applies **every conversation patch in the project** to the live game files so you
can launch the game and verify dialogue in context. Before writing anything, the
editor makes a lightweight per-file backup of the affected conversations so they
can be precisely restored afterwards.

New conversations (created with the **⊕** button and not yet on disk) are written
as blank templates at this point, then patched normally.

While the game files are patched, a modal dialog blocks the editor — this is
intentional to prevent inconsistent edits during testing.

**Conflict detection:** If a game file has changed since your patch was created
(e.g. after a game update), the editor shows a conflict dialog with the affected
node, field, and the expected vs. actual values. You can choose **Force Apply**
to apply your patch's target value anyway, or **Cancel Test** to leave game files
unchanged.

---

### 6 — Restore after testing

**Test › Restore Conversation** (`F6`)

Reverts the patched game files to their pre-test state and unlocks the editor.
New conversations that were written by `F5` are deleted again.
The project file is unchanged; your edits are ready for the next test cycle.

If the editor crashes or is force-quit while in test mode, the next launch detects
the incomplete state and shows the modal automatically so you can restore before
editing.

---

### 7 — Distribute and combine patches

#### Single patch

Share your `.dialogproject` file. Recipients open the **Patch Manager** (see below)
to apply it, or use the [`dialog-patcher` CLI](#dialog-patcher-cli) for scripted installs.

#### Combining patches from multiple authors

**File › Merge Projects…**

Merges one or more other `.dialogproject` files into the currently open project.
Patches for the same conversation are merged field-by-field; the last file you
pick wins on any contested field. Layouts are merged preserving both sets of
positions. The merged file is saved immediately.

#### Patch Manager — load order and conflict detection

**File › Patch Manager…** (also available as a standalone `DialogEditor.PatchManager.exe`)

Lets you maintain an ordered stack of `.dialogproject` files, detect conflicts
before applying, and write all patches to the game folder in one step:

1. **Add project(s)…** — pick one or more `.dialogproject` files
2. **Reorder** with ↑ ↓ — entries lower in the list win on any conflict
3. The **conflict panel** identifies every contested field before you apply
4. **Apply patches** — writes every conversation patch to the selected game folder
5. **Save / Load load order** — persists the list as a `.patchlist` file;
   double-clicking a `.patchlist` opens the standalone app directly

The standalone app requires no editor installation and is suitable for end users
applying community mods.

---

## dialog-patcher CLI

A lightweight command-line tool for scripted or automated patch application,
suitable for mod installers, CI pipelines, and power users.

```
dialog-patcher <game-dir> <project.dialogproject> [project2 ...] [options]
```

Multiple project files are merged in order before being applied (later project
wins on any contested field), matching the Patch Manager's load-order semantics.

| Option | Description |
|--------|-------------|
| `-f`, `--force` | Apply patches even when a field's baseline doesn't match — use after a game update |
| `-v`, `--verbose` | Print each conversation as it is patched |
| `-q`, `--quiet` | Suppress all output except errors; only the exit code signals success |
| `--dry-run` | List what would be patched without writing any files |
| `--version` | Print version and exit |
| `-h`, `--help` | Show usage |

**Exit codes:** `0` success · `1` conflict (re-run with `--force`) · `2` error

**Examples:**

```sh
# Apply a single mod
dialog-patcher "C:/GOG Games/Pillars of Eternity" my_mod.dialogproject

# Apply two mods in load order, verbose
dialog-patcher "C:/PoE2" base_mod.dialogproject override_mod.dialogproject --verbose

# Validate without touching game files
dialog-patcher "C:/PoE2" my_mod.dialogproject --dry-run

# Scripted install — check exit code
if dialog-patcher "$GAME_DIR" "$MOD_FILE" --quiet; then
    echo "Patched OK"
else
    echo "Patch failed" >&2
fi
```

**Publishing a self-contained executable** (no .NET installation required on the
target machine):

```sh
dotnet publish DialogEditor.PatchCli -r win-x64 --self-contained -o dist/
```

---

## Settings

**File › Settings** (`Ctrl+,`)

| Setting | Purpose |
|---------|---------|
| Backup directory | The folder chosen during first-time setup where the original game files are stored. Used for full disaster recovery via **Test › Restore Backup** (`Ctrl+Shift+B`). Can be changed here if you want to move the backup to a different location. |

---

## Keyboard Shortcuts

Press **`?`** in the editor to open the floating Legend window, which lists all
shortcuts alongside connection types, node colours, and canvas controls. The
Legend window can be moved outside the editor (useful on a second monitor).

| Key | Action |
|-----|--------|
| `Ctrl+N` | New project |
| `Ctrl+O` | Open project |
| `Ctrl+S` | Save project |
| `Ctrl+Shift+O` | Open game folder |
| `Ctrl+,` | Settings |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |
| `Ctrl+F` | Focus node search |
| `Ctrl+H` | Find / Replace |
| `Delete` | Delete selected node |
| `F5` | Test Patch |
| `F6` | Restore Conversation |
| `Ctrl+Shift+B` | Restore Backup (all conversations) |
| `Escape` | Clear search / close browser flyout |
