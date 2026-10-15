# Nexus Mods page — Pillars Dialog Editor

The text for the editor's Nexus Mods pages (#73), plus the release announcement. There are two
pages with the same content, one per game, because modders browse each game's section
separately. Keep this file as the source: edit it here, then paste it into both pages.

| | Pillars of Eternity | Pillars of Eternity II: Deadfire |
|---|---|---|
| Game on Nexus | `pillarsofeternity` | `pillarsofeternity2` |
| Page URL | *(fill in once created)* | *(fill in once created)* |

When both pages exist, put their URLs in the table above and in the README's Get started
section.

## Page fields

- **Name:** Pillars Dialog Editor
- **Category:** Modding Tools
- **Version:** the `VERSION` of the release being uploaded
- **Author:** your Nexus username (the tool is a solo project)
- **Brief overview** (Nexus limits this field's length; this is under 300 characters):

  > A conversation editor for Pillars of Eternity and Deadfire. Edit lines, choices,
  > conditions and scripts on a node canvas, test in-game with one key, and share the
  > result as a mod. Your game files are backed up and can always be restored.

- **Images:** a screenshot of the conversation canvas, the same one as at the top of the
  README (#70). Not chosen yet.
- **Main file:** `PillarsDialogEditor-<ver>.zip` from the matching `v<ver>` GitHub release.
  Upload the same zip, don't rebuild it.
- **Requirements:** none. The zip includes its own .NET runtime. Git is optional and only
  needed for the version-control tools.
- **Permissions:** match the repository's `LICENSE`.

## Description

Paste this into the description editor's BBCode view.

```bbcode
[size=5][b]Pillars Dialog Editor[/b][/size]

A conversation editor for [i]Pillars of Eternity[/i] and [i]Pillars of Eternity II: Deadfire[/i]. Open your game folder, pick any conversation, and change what people say, which choices the player gets, and what happens when they pick one.

[b]Your game stays safe.[/b] Edits are saved in a project file, never straight into the game. Before anything touches a game file, the editor backs up the original, and one click puts it back.

[size=4][b]Getting started[/b][/size]
[list=1]
[*]Download and unzip the editor anywhere, then start [b]DialogEditor.Avalonia.exe[/b]. Nothing needs installing.
[*]Choose [b]File › Open Game Folder…[/b] and pick the folder the game is installed in.
[*]Choose [b]Help › Create Sample Project…[/b] and follow [b]Help › Open Walkthrough…[/b]. It takes you from your first edit to seeing it in the game.
[/list]

[size=4][b]Features[/b][/size]
[list]
[*][b]Node canvas[/b] for every conversation in both games, including the DLC: lines, player choices, conditions and scripts, with search, spell checking and a minimap.
[*][b]Test in-game with one key[/b] (F5), then restore the original conversation just as quickly.
[*][b]Translations:[/b] export your mod's text as CSV, JSON or XLIFF for translators, and import it back.
[*][b]Deadfire voice-over:[/b] import lines one by one or in a batch, preview them, and check that every line has its audio.
[*][b]Checks before you ship:[/b] text tags and markup, voice-over, reputation and disposition balance, and flow analytics such as dead ends and unreachable lines.
[*][b]Find in project[/b], batch find and replace, and every line a speaker has across the game.
[*][b]Version control[/b] (optional, needs Git): history, compare versions, line-by-line attribution and branches, for working alone or with other writers.
[*][b]Share your mod[/b] as a [b].dialogpack[/b] with [b]File › Export Mod Bundle…[/b]. Players install it with the [b]Pillars Dialog Patcher[/b], which can combine several mods and warns about conflicts.
[/list]

The full guide is on GitHub:
[url=https://github.com/kjmikkel/PillarsDialogEditor#readme]README[/url] · [url=https://github.com/kjmikkel/PillarsDialogEditor/blob/main/docs/walkthrough.md]Walkthrough[/url]

[size=4][b]Releasing your mod[/b][/size]
List the [b]Pillars Dialog Patcher[/b] page as a requirement on your mod's page, and upload the [b].dialogpack[/b]. Players who already have the patcher only need your file.

[size=4][b]Bugs and source code[/b][/size]
The editor is free and open source. Report problems on [url=https://github.com/kjmikkel/PillarsDialogEditor/issues]GitHub[/url]; the source and every release are there too.

[size=4][b]Not affiliated with Obsidian[/b][/size]
Pillars Dialog Editor is a free fan-made tool. It is not made, endorsed or supported by Obsidian Entertainment or the games' publishers. [i]Pillars of Eternity[/i] and [i]Pillars of Eternity II: Deadfire[/i] are trademarks of their respective owners. The editor contains no game files: it reads and changes the files of a copy of the game you own. Please don't contact Obsidian about problems with mods or with this tool.
```

## On each editor release

When a `v<ver>` release is published, on **both** pages:

1. Upload the release's `PillarsDialogEditor-<ver>.zip` as the new main file, and move the
   previous one to *Old versions*.
2. Set the page's version to `<ver>`.
3. Add a changelog entry that links to the GitHub release.
4. If the canvas looks different, retake the screenshot and replace the image.

These steps are also in the [release checklist](../release-checklist.md).

## Release announcement

For the 1.0 release, post this where *Pillars* modders gather: the Nexus pages' own posts tab,
and community forums or Discord servers whose rules allow tool announcements. Fill in the
links before posting.

```text
Pillars Dialog Editor 1.0 is out: a free conversation editor for Pillars of Eternity and Deadfire.

Open your game folder, pick any conversation, and change the lines, the player's choices and what they trigger, on a node canvas. Press F5 to try your change in the game, and restore the original just as quickly. Before the editor touches a game file it backs up the original, so nothing is permanent.

It also handles translations (CSV, JSON or XLIFF), Deadfire voice-over, checks for broken text tags and unreachable lines, and optional Git-based history for writing with others.

Mods are shared as .dialogpack files. Players install them with the separate Pillars Dialog Patcher, which combines several mods and warns about conflicts.

- Editor: <Nexus link> · <GitHub release link>
- Patcher: <Nexus link>
- Source, guide and bug reports: https://github.com/kjmikkel/PillarsDialogEditor

It's a solo, unofficial fan project, not made or endorsed by Obsidian Entertainment. Feedback and bug reports are very welcome.
```
