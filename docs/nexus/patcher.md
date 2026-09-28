# Nexus Mods page — Pillars Dialog Patcher

The text for the patcher's own Nexus Mods pages (#81). There are two pages with the same
content, one per game, so that a conversation mod for either game can list the patcher as
a **requirement**. Keep this file as the source: edit it here, then paste it into both pages.

| | Pillars of Eternity | Pillars of Eternity II: Deadfire |
|---|---|---|
| Game on Nexus | `pillarsofeternity` | `pillarsofeternity2` |
| Page URL | *(fill in once created)* | *(fill in once created)* |

When both pages exist, put their URLs in the table above. Also add them to the
player guide's update instructions (next to the GitHub releases link) and to the
mod-author section of the main `README.md`, so mod authors can copy them.

## Page fields

- **Name:** Pillars Dialog Patcher
- **Category:** Utilities
- **Version:** the `PATCHER_VERSION` of the release being uploaded
- **Author:** your Nexus username (the tool is a solo project)
- **Brief overview** (Nexus limits this field's length; this is under 300 characters):

  > Installs and removes conversation mods (.dialogpack) for Pillars of Eternity and
  > Deadfire. Load order, conflict warnings, and one click to restore the original game
  > files. Needed by mods made with the Pillars Dialog Editor.

- **Images:** `docs/patcher/images/patch-manager.png` (primary) and
  `docs/patcher/images/conflicts.png`.
- **Main file:** `PillarsDialogPatcher-<ver>.zip` from the matching `patcher-v<ver>`
  GitHub release. Upload the same zip, don't rebuild it.
- **Requirements:** none. The zip includes its own .NET runtime.
- **Permissions:** match the repository's `LICENSE`.

## Description

Paste this into the description editor's BBCode view.

```bbcode
[size=5][b]Pillars Dialog Patcher[/b][/size]

Installs conversation mods for [i]Pillars of Eternity[/i] and [i]Pillars of Eternity II: Deadfire[/i]. If a mod's page lists this tool as a requirement, this is what you need to install that mod.

[b]Nothing is permanent.[/b] Before the patcher changes a game file, it keeps a copy of the original. You can remove every mod and get the original game files back at any time.

[size=4][b]Installing a mod[/b][/size]
[list=1]
[*]Download and unzip the patcher anywhere, for example to your Desktop.
[*]Start [b]DialogEditor.PatchManager.exe[/b].
[*]Next to [b]Game folder[/b], click [b]Browse…[/b] and choose the folder the game is installed in.
[*]Click [b]Add mods…[/b] and choose the mod's [b].dialogpack[/b] file.
[*]Click [b]Apply patches[/b] and start the game.
[/list]

[size=4][b]Features[/b][/size]
[list]
[*][b]Several mods at once[/b], in a load order you choose. When two mods change the same line, the one lower in the list wins.
[*][b]Conflict warnings before anything is installed[/b]: the Patch Manager names the conversation, the line and both mods.
[*][b]Remove one mod or all of them.[/b] Every apply starts from the original files, and [b]Remove all mods[/b] puts every original back.
[*][b]Game updates[/b] are detected. The patcher asks before treating updated files as the new originals.
[*][b]Voice-over and translations[/b] that come with a mod are installed too.
[*][b]Command-line version[/b] ([i]dialog-patcher.exe[/i]) for mod installers and scripts.
[/list]

The full player guide, with where to find the game folder on Steam and GOG and what to do when something goes wrong, is the [b]README.md[/b] in the download, and also on GitHub:
[url=https://github.com/kjmikkel/PillarsDialogEditor/blob/main/docs/patcher/README.md]Player guide[/url]

[size=4][b]For mod authors[/b][/size]
Make conversation mods with the [url=https://github.com/kjmikkel/PillarsDialogEditor]Pillars Dialog Editor[/url]. Use [b]File › Export Mod Bundle…[/b] to produce a [b].dialogpack[/b], and add this page as a requirement on your mod's page.

[size=4][b]Bugs and source code[/b][/size]
The patcher is free and open source. Report problems on [url=https://github.com/kjmikkel/PillarsDialogEditor/issues]GitHub[/url]; the source and every release are there too.

[size=4][b]Not affiliated with Obsidian[/b][/size]
Pillars Dialog Patcher is a free fan-made tool. It is not made, endorsed or supported by Obsidian Entertainment. [i]Pillars of Eternity[/i] and [i]Pillars of Eternity II: Deadfire[/i] are trademarks of their respective owners. Please don't contact Obsidian about problems with mods or with this tool.
```

## On each patcher release

When a `patcher-v<ver>` release is published, on **both** pages:

1. Upload the release's `PillarsDialogPatcher-<ver>.zip` as the new main file, and move
   the previous one to *Old versions*.
2. Set the page's version to `<ver>`.
3. Add a changelog entry that links to the GitHub release.
4. If the Patch Manager's window changed, retake the screenshots and replace the images.

This belongs in the release checklist (#68).
