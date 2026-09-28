# .dialogpack format

A `.dialogpack` file is a standard ZIP archive. Rename it to `.zip` to
inspect or extract its contents with any archive tool.

## Contents

- `project.dialogproject` — the dialog diff (JSON); apply with Pillars Dialog Patcher
  (its Patch Manager or its `dialog-patcher` CLI).
- `vo/` — voice-over audio files in Wwise `.wem` format, laid out to
  mirror the game's VO directory structure. Present only when the mod
  contains voice-over; the Patch Manager and CLI copy these to the
  correct game folder location when applying the pack.
- `FORMAT.md` — this file.

## Applying a .dialogpack

You need **Pillars Dialog Patcher**. Download it from the mod's requirements on
Nexus Mods, or from the `patcher-v…` releases at
<https://github.com/kjmikkel/PillarsDialogEditor/releases>. Its player guide, with
steps for installing and removing mods, is the `README.md` in the patcher's folder.

**GUI:** Start `DialogEditor.PatchManager.exe`, click **Add mods…** and pick the
`.dialogpack` file, choose your game folder, and click **Apply patches**.

**CLI:**
```
dialog-patcher <game-dir> mymod.dialogpack
```

## Voice-over directory structure

`vo/` mirrors `PillarsOfEternityII_Data/StreamingAssets/Audio/Windows/Voices/English(US)/`:
```
vo/
  eder/
    my_line_0001.wem
    my_line_0001_fem.wem
  narrator/
    my_line_0002.wem
```
