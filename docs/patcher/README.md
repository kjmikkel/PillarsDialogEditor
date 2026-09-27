# Pillars Dialog Patcher

Installs dialogue mods for *Pillars of Eternity* and *Pillars of Eternity II: Deadfire*.
Mods come as `.dialogpack` (or `.dialogproject`) files. You don't need the Pillars Dialog Editor
to install them.

## What's in this folder

| File | What it's for |
|---|---|
| `DialogEditor.PatchManager.exe` | **Start here.** The Patch Manager: add mods, choose your game folder, apply. |
| `cli\dialog-patcher.exe` | The command-line version, for mod installers and scripts. Run `dialog-patcher --help` in a terminal. |

## Installing a mod

1. Start `DialogEditor.PatchManager.exe`.
2. Add the mod's `.dialogpack` file.
3. Choose your game folder and apply.

The patcher keeps a copy of every game file it changes, so you can always remove all mods again and
get the original files back from the Patch Manager.

## "This mod needs a newer patcher"

The mod was made with a newer editor than this patcher understands. Nothing was changed. Download the
latest Pillars Dialog Patcher (releases named `patcher-v…`):
<https://github.com/kjmikkel/PillarsDialogEditor/releases>
