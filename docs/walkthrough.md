# Beginner Walkthrough — Your First Hour with the Pillars Dialog Editor

New to dialogue modding, or to version control? Start here. This guide walks you through
the whole editor on a **sample project** the app builds for you — so you can click every
button, make mistakes, and undo them, without touching your real game or your real work.

> **The safety promise.** Everything in this guide happens inside a throwaway sample folder
> and your game's backup safety net. Nothing you do here changes the game permanently, and
> you can delete the sample folder whenever you like. Relax and experiment.

This guide uses **Eder**, a companion you meet early in both games:
- **Pillars of Eternity (PoE1):** his first meeting, in Gilded Vale (`companion_cv_eder_intro`).
- **Pillars of Eternity II: Deadfire (PoE2):** his companion conversation hub (`companion_eder_hub`).

---

## 1. Open your game folder

**File ▸ Open Game Folder** (`Ctrl+Shift+O`), then pick the root folder of your PoE1 or PoE2
installation. The conversation list fills in on the left.

The **first time** you open a game folder, the editor asks where to keep a **backup** of the
original game files, then copies them there. This backup is what makes testing safe — keep
the default location unless you have a reason to change it.

---

## 2. Create the sample project

**Help ▸ Create Sample Project…**, then choose an **empty** folder just for the sample (make
a new one, e.g. `Eder-sample`).

The editor builds a small practice project based on the Eder conversation and opens it. It
has already made a few example edits for you — a changed line, a new line, a removed line,
and a translator note — and, if you have **Git** installed, a little practice **history**
with two branches (`main` and `experiment`). You'll explore all of that below.

> No Git installed? The sample still opens and everything except the version-control section
> works. To try those tools too, install Git (see the README) and re-create the sample.

---

## 3. Browse and edit

Click the Eder conversation in the list to open it on the canvas.

- **Select a node:** click it. Its details appear on the right.
- **Change a line:** edit the **Default text** field. Watch the node update on the canvas.
- **Add a node:** double-click empty canvas space, then drag from an existing node's output
  port to your new node to connect them.
- **Delete a node:** select it and press `Delete`.

> **PoE2 only:** in the node details, the **Speaker / Listener** fields offer a name picker,
> so you can choose characters by name rather than by ID. (PoE1 uses raw IDs.)

Made a mess? `Ctrl+Z` undoes anything. You truly cannot break the game from here.

---

## 4. Save to the project

**File ▸ Save Project** (`Ctrl+S`). Your edits are written into the `.dialogproject` file as a
compact diff. The game files are **not** touched — saving only updates your project.

---

## 5. Test in the game, then restore

**Test ▸ Test Patch** (`F5`) applies your project to the live game files so you can launch
the game and see your dialogue in context. Before writing anything, the editor backs up the
affected files.

When you're done, **Test ▸ Restore Conversation** (`F6`) puts the game files back exactly as
they were. Nothing permanent, every time.

---

## 6. Translate (optional)

**File ▸ Export for Translation…** writes a file listing every edited line, including the
**translator note** the sample added — that's the "writer comment" column that gives a
translator context. Fill in the translated columns, then **File ▸ Import Translation…** to
bring them back. Translations live inside the same project file.

---

## 7. Trying version control safely

This is the part that feels mysterious until you've poked at it. The sample gave you a ready
made history so the tools have something to show. All of these live under the **Edit** menu,
and none of them can harm your game or your real projects.

> Version control (Git) records your project as a series of saved snapshots ("commits") so you
> can see what changed, go back, and try ideas on separate "branches." These tools are
> optional — if Git isn't installed, they simply tell you so.

- **Edit ▸ Branches…** — you'll see two branches: `main` and `experiment`. Select
  `experiment` and **Switch** to it, then switch back to `main`. Notice the canvas reload —
  that's the project changing to match each branch. The current branch can't be deleted, and
  deleting an unmerged branch asks you to confirm first.
- **Edit ▸ History…** — the timeline of commits the sample created. Select one to open it in
  Compare.
- **Edit ▸ Attribution…** — "who last edited each node," drawn from the history. Because the
  sample's edits were spread across commits, different nodes show different commits.
- **Edit ▸ Compare Versions…** — pick two points (e.g. `main` vs `experiment`) to see exactly
  what differs, with added/changed/removed nodes colour-coded. You can selectively bring
  changes from one side into your copy.

Switch branches, compare, and browse history as much as you like — it's all happening inside
the sample folder.

---

## 8. Where to go next

You've now done the full loop: open, edit, save, test, translate, and version-control — on a
sample that can't hurt anything. When you're ready for real work:

- Read the **[README](../README.md)** for the complete reference on every feature.
- Use the **Patch Manager** (File ▸ Patch Manager…) to combine and apply finished mods.
- Delete the sample folder whenever you're done with it.

Happy modding.
