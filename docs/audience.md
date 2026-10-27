# Who the tools are for

This page names the people Pillars Dialog Editor and Pillars Dialog Patcher are built for.
Use it when deciding what a feature should include, and as the audience for any UI/UX review:
judge a screen against the people listed here, not against a generic user.

## Pillars Dialog Editor

### Primary audiences

These two decide how a screen should look and behave.

**The Reader** opens the games' conversations to read them: lore, companion banter, branches
they missed while playing. They never edit. They come to find a conversation, read it and
follow its branches. They give up if the screen is crowded with editing tools they don't
understand, or if they're afraid that clicking around will break their game.

**The Modder** makes large edits and writes whole new conversations, then ships them to
other players as mods. They spend hours at a time in the editor. They come to build,
check and package their work. They give up if common actions are slow, if they're buried in
menus with no keyboard route, or if they can't trust that what they ship will load in the
game.

### Secondary audiences

Their needs count when meeting them costs the primary audiences nothing. When there's a
conflict, the primary audiences win.

**The Fixer** corrects typos, rewords a line here and there, or fixes a small bug such as a
wrong condition or a broken link. Their edits are small and targeted. They want to find the
line fast, change it safely and get it into the game without learning the whole workflow.

**The Curious** want to see how things work behind the scenes: conditions, scripts, flags,
speakers, how a conversation is wired together. They mostly read, but at a technical
level, so internal names and raw values should be visible and explained, not hidden.

### People move up the ladder

The roles form a ladder: Reader, then Curious or Fixer, then Modder. Someone who starts out
reading may end up making mods. The editor should let them take each step without hitting a
wall or having to relearn the tool.

### When the Reader and the Modder conflict

Each audience gets screens built for them: a separate **Reading mode**
([#234](https://github.com/kjmikkel/PillarsDialogEditor/issues/234)), switched with the
Reading/Editing badge in the toolbar or View > Reading mode.

- **Reading mode** is for the Reader. It is read-only by construction: the editing menus,
  shortcuts and tools are removed or switched off, so nothing a Reader clicks can change
  a game file, a project or a mod. The Conversations, Canvas and Node Details panes stay, plus
  the read-only tools a Reader or the Curious can use (Speaker Lines, Reputation & Disposition
  Balance, Condition search).
- **Editing mode** is the full editor, and **the Modder wins there**.
- A first-run chooser asks which one to start in; closing it picks Reading, the safe choice.
  Someone who moves up the ladder switches with one click, and the editor asks what to do with
  unsaved changes first.

Two things always count as defects, in either mode:

- A Reader must never be able to change a game file, or their own work, by accident.
- Nothing a Reader needs (opening a game folder, finding a conversation, reading it and
  following its branches) may require knowing how editing, projects or diffs work.

## Pillars Dialog Patcher

### Primary audience

**The Player** installs mods other people have made and makes none themselves. They come to
get a mod into their game and play. They give up if installing takes more than a few clear
steps, if error messages assume modding knowledge, or if they can't tell whether the mod
worked or how to undo it.
