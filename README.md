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
> conversation freely. Editing — changing text, adding nodes, modifying links —
> requires an open project. The status bar will remind you when you are in
> read-only mode.

---

### 3 — Browse and edit

Click any conversation in the browser to open it on the canvas.

| Action | How |
|--------|-----|
| Select a node | Click it on the canvas |
| Edit text, identity, voice, links | Use the detail panel on the right |
| Add a node | Double-click the canvas background |
| Connect two nodes | Drag from a node's output port to another node's input port |
| Delete a node | Select it, press `Delete`; or right-click › Delete node |
| Add a connected node | Right-click › Add connected node |
| Delete a connection | Right-click the connection line › Delete connection |
| Search nodes | `Ctrl+F`, or type in the search box above the canvas |
| Undo / Redo | `Ctrl+Z` / `Ctrl+Y` — all edits are fully undoable within a session |

Use the **`?`** button to open the floating Legend window for a full reference of
node colours, connection types, symbols, and keyboard shortcuts. It can be moved
to a second monitor and left open while you work.

---

### 4 — Save to project

**File › Save Project** (`Ctrl+S`)

Records the current conversation's changes as a diff inside the open project file.
The diff captures only what changed — adding, removing, or editing nodes and links.
If you add a node and then delete it before saving, no trace of it appears in the
diff. Game files are not modified. The project file is plain JSON and can be
version-controlled or shared freely.

---

### 5 — Test in-game

**Test › Test Patch** (`F5`)

Applies **every conversation patch in the project** to the live game files so you
can launch the game and verify dialogue in context. Before writing anything, the
editor makes a lightweight per-file backup of the affected conversations so they
can be precisely restored afterwards.

While the game files are patched, a modal dialog blocks the editor — this is
intentional to prevent inconsistent edits during testing.

---

### 6 — Restore after testing

**Test › Restore Conversation** (`F6`)

Reverts the patched game files to their pre-test state and unlocks the editor.
The project file is unchanged; your edits are still there, ready for the next
test cycle or final save.

If the editor crashes or is force-quit while in test mode, the next launch detects
the incomplete state and shows the modal automatically so you can restore before
editing.

---

### 7 — Distribute

Share your `.dialogproject` file. Recipients apply it by:

1. Installing this editor (or the planned standalone patch utility)
2. Opening the matching game folder
3. Opening the project file
4. Running **Test › Test Patch** — or, once the standalone utility ships,
   running it directly without the full editor

Multiple projects can be **stacked in load order**: each patch is applied on top
of the previous, with conflict detection if two patches modify the same field.

---

## Settings

**File › Settings** (`Ctrl+,`)

| Setting | Purpose |
|---------|---------|
| Backup directory | The folder chosen during first-time setup where the original game files are stored. Used for full disaster recovery via **Test › Restore Backup** (`Ctrl+Shift+B`). Can be changed here if you want to move the backup to a different location. |

---

## Keyboard Shortcuts

Press **`?`** in the editor to open the floating Legend window, which lists all
shortcuts alongside connection types, node colours, and canvas controls.

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
| `Delete` | Delete selected node |
| `F5` | Test Patch |
| `F6` | Restore Conversation |
| `Ctrl+Shift+B` | Restore Backup (all conversations) |
| `Escape` | Clear search / close browser flyout |
