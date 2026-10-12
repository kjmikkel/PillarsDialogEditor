# Canonical mod — poe1

**Generated — do not edit by hand.** The scenario in `DialogEditor.Tests/CanonicalMod/` builds
this mod through the editor's own view models ([#138](https://github.com/kjmikkel/PillarsDialogEditor/issues/138)).
`CanonicalModGoldenTests` fails when a fresh build differs from these files. If the editor's
output changed on purpose, run that test with `DIALOGEDITOR_REGENERATE_CANONICAL_MOD=1`, review
the diff and commit it.

The mod edits the `canonical` conversation of [`../../Canonical/poe1`](../../Canonical/poe1) and adds a
new one. To try it in the app, copy that fixture folder somewhere outside the repo, choose the copy as
the game folder, and apply `canonical_mod.dialogpack` with the Patch Manager. All text, GUIDs,
variable names and voice-over bytes are invented; nothing comes from game data.

## Features

Each row is one scenario step. Its expectation is checked after applying the mod through
`dialog-patcher`, the Patch Manager and Test Patch (F5).

| Feature | What the mod does | Known issue |
|---|---|---|
| `nodes.add-npc-line` | New NPC line 11, linked from 10 |  |
| `nodes.add-player-choice` | New player choice 12 under 0 |  |
| `nodes.add-script` | New script node 13 after 12 |  |
| `nodes.convert-player-to-npc` | Player choice 3 converted to an NPC line with the detail pane's Node Type control, NPC speaking to the player |  |
| `nodes.delete` | Node 9 deleted; bank 5 keeps child 8 |  |
| `kept.bank-and-trigger` | Bank 5 and trigger 6 get comments; their bank and trigger fields survive |  |
| `links.remove` | Link 2→5 removed |  |
| `links.weight` | Link 0→2 re-weighted to 3 |  |
| `links.display-values` | 0→1 ShowNever, 0→2 ShowOnce, 0→12 ShowAlways |  |
| `conditions.node-nested` | Node 12: A and (B or not C), each a multi-parameter call |  |
| `conditions.replace-nested` | Node 1's nested condition replaced by one negated call (PoE1 links carry no conditions) |  |
| `conditions.clear` | Node 2's condition removed entirely |  |
| `scripts.add-all-categories` | Node 13 gets OnEnter, OnExit and OnUpdate scripts |  |
| `scripts.add-to-existing` | Node 4 gains a second OnExit script; its three existing scripts stay |  |
| `scripts.remove` | Node 0's OnEnter script removed |  |
| `props.display-and-persistence` | Every DisplayType and Persistence value: 11 Hidden/OnceEver, 12 Bark/OncePerConversation, 13 Overlay/MarkAsRead, 8 Conversation, 3 None |  |
| `props.speaker-listener` | Node 10's speaker becomes the narrator and its listener the NPC |  |
| `props.comments-and-direction` | Node 11 gets comments and an actor direction; node 10's comments and direction are cleared |  |
| `text.edit-with-female` | Node 0's text and its female variant edited (keeping the [Player Name] token) |  |
| `text.markup` | Node 12's text uses <i> markup |  |
| `text.second-language` | German text for new node 11 and edited node 0; English stays as edited |  |
| `vo.filename` | Node 11's VOFilename set |  |
| `conversation.new` | New conversation canonical_mod_new: NPC → two player choices (one conditioned) → end, in en and de |  |
