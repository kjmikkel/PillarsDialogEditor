# Conversations without a start node — design

GitHub issue 172. A follow-up from the final review of issue 162.

## Goal

When a conversation has nodes but no node 0, the editor says so clearly. For conversations
created in the editor, it offers a one-step, undoable fix that makes a chosen node the start
node.

## Why node 0

Both games start a conversation at node 0 unless a script names another node. Deadfire's and
PoE1's `StartConversation` both default `nodeID` to 0 (issue 162). A conversation without a
node 0 can't be started from its first line.

Since issue 162, new and imported conversations start at node 0. These still don't:
- new conversations created before issue 162, which start at node 1;
- conversations whose node 0 was deleted;
- CSV and JSON imports, which keep the IDs in the imported file.

## What the editor does today

- **Flow Analytics** (`FlowAnalysisService`) walks the graph from node 0. With no node 0,
  it flags **every** node as Unreachable.
- **Path Stats** (`PathStatsService`) roots at node 0. With no node 0, it shows zero path
  figures without saying why.
- **The canvas:** the ⌂ "Jump to root" button and keyboard selection fall back to the first
  node. The canvas shows nothing about the problem.

## Scope

Two kinds of conversation are treated differently:

| | Created in the editor (`DialogProject.IsNewConversation`) | Shipped game conversation |
|---|---|---|
| Diff baseline | empty: every node is an added node | the game's file |
| Renumbering a node | rewrites the project's own data only | becomes "delete node N, add node 0"; the original file's unmodelled fields (bank `ChildNodeIDs`, trigger `StartNodeID`, …) are lost, and every incoming link is deleted and re-added |
| Offered | the warning **and** the fix | the warning only |

A shipped conversation only lacks node 0 if the user deleted it. The fix for that is Undo, or
removing the deletion from the patch, and the warning says so.

**Out of scope:** rewriting references from other conversations to the renumbered node's old
ID. That covers `StartConversation` calls that name the old ID explicitly, and `ExternalVO`
aliases. The fix reports how many there are in the project (§5) so the user can fix them.

## Design

### 1. Core: what counts as a start node

`StartNode.Id = 0` is a constant, with the comment explaining why. Flow Analytics and Path
Stats use it instead of the literal.

A conversation **lacks its start node** when it has at least one node and none of them has ID
0. An empty conversation doesn't lack one: its first node will be node 0 (`NodeIdAllocator`).

### 2. Flow Analytics and Path Stats

- `FlowAnalysisReport` gains `bool HasStartNode`.
  - When it is false, no node gets `Unreachable`.
  - Every other check runs as before: dead ends, empty text, no incoming links, scripts that
    never run, barks, and so on.
  - `NoIncomingLinks` is unchanged. In a conversation without node 0, the node that should
    be the start is exactly one with no incoming links, so the issue points at it.
- `FlowAnalyticsViewModel` shows one error row at the top of the issue list while
  `HasStartNode` is false:
  - label `FlowAnalytics_Issue_NoStartNode`: "No start node: the game starts this
    conversation at node 0, which doesn't exist";
  - a tooltip explaining the fix: for a conversation created in the editor, "Choose start
    node…" on the canvas; otherwise, Undo or remove the deletion from the patch.
  - The row is not tied to a node, so clicking it selects nothing.
- `PathStatsReport` gains `bool HasStartNode`.
  - When it is false, the Path Stats panel shows `PathStats_NoStartNode` instead of zero
    figures.
  - The word totals and words per speaker don't depend on the start node, and are still
    shown.

### 3. The edit: `MakeStartNodeCommand`

One `IEditCommand` on the existing `UndoRedoStack`. Execute and Undo are exact inverses.

**Preconditions.** If any of these fails, nothing changes and the status bar says why. The
command is never pushed.
- The conversation is editable (`IsEditable`).
- It is created in the editor (`CanChooseStartNode`, §4).
- It has no node 0.
- The node is in the conversation.
- If the node has a staged VO file (see VO below), no file already has the target name.

**What it changes** from the chosen node's old ID *N* to 0:

| Data | Where it lives while editing | How it moves |
|---|---|---|
| The node's ID | `NodeViewModel.NodeId` (get-only today) | an internal setter used only by this command, raising `PropertyChanged` |
| Links in and out | connections point to node objects; IDs are only worked out when a snapshot is taken | nothing to do |
| Layout position | the node object's position | nothing to do |
| Primary-language text | the node's `StringEntry` | re-keyed with the node; the plan confirms the snapshot reads the ID from the node and not from the entry |
| Node comment | `ConversationViewModel._nodeComments[N]` | moved to key 0 |
| Translations in other languages | the project's `ConversationPatch.Translations`, not on the canvas | via the remap log below |
| Staged VO | `_vo/<prefix>/<conv>_NNNN.wem` and `…_fem.wem` | renamed to `…_0000`, and renamed back on Undo |

**Translations: the remap log.** At save time, `FoldCanvasIntoProject` copies the other
languages from the stored patch, then drops entries whose node no longer exists. Without a
remap, a renumber would therefore silently delete the node's translations.
- `ConversationViewModel` keeps an ordered list of pending ID remaps.
  - Execute appends *N → 0*.
  - Undo appends *0 → N*.
- `FoldCanvasIntoProject` applies the pending remaps, in order, to the carried translations
  before it filters them, then clears the list.
- Because the log records Undo as a remap too, Undo still works after a save, when the
  stored patch already uses key 0.

**VO.**
- A node has a staged file when `ExternalVO` is empty and `_vo/` holds the default-named
  file (`VoPathResolver.ExpectedRelativePath`).
- When `ExternalVO` is set, the path is explicit and nothing is renamed.
- The rename is a file move inside the project's `_vo/` folder:
  - Execute moves the files to the new name, and Undo moves them back.
  - If a move fails, the command reverts what it has already done, logs it through
    `AppLog.Error`, and reports it in the status bar.
- A copy that Test Patch synced into the game folder is still tracked under its old name, so
  F6 removes it as before. The next F5 syncs the renamed file.

The undo description is `Undo_MakeStartNode`: "Make node {0} the start node".

### 4. View models

**`ConversationViewModel`:**
- `HasStartNode`: false only when the conversation has nodes and none has ID 0. It is
  recomputed when a node is added, deleted or renumbered, including by Undo and Redo.
- `CanChooseStartNode`: set by `MainWindowViewModel` when it loads a conversation, as for
  `IsEditable`. It is true when the conversation is new to the project.
- `ShowStartNodeWarning` = `!HasStartNode`.
- `IsChoosingStartNode`: the pick mode.
  - `ChooseStartNodeCommand` turns it on. It can run only when `CanChooseStartNode`,
    `IsEditable` and `!HasStartNode` are all true.
  - `CancelChooseStartNodeCommand` turns it off.
  - While it's on, selecting a node runs `MakeStartNodeCommand` on that node and ends the
    mode.
  - The mode also ends if the conversation is switched, or if node 0 appears some other way.
- `MakeStartNodeCmdCommand(NodeViewModel)`: the right-click item. Its `CanExecute` follows
  the same rule as `ChooseStartNodeCommand`.
- `NodeRenumbered(oldId, newId)` event: `MainWindowViewModel` handles it for the status
  message (§5).

### 5. Status message and references elsewhere

After a renumber, `MainWindowViewModel` uses a new `StartNodeReferenceScanner` to scan the
project's other conversations.
- **Inputs:** the stored patches' added and modified nodes, plus the open canvas's snapshot
  when the open conversation is another one.
- **It counts:**
  - `StartConversation` and `StartConversationFacingListener` calls, recognised by
    `ConversationJumpVerbs`, that name this conversation and the old node ID;
  - `ExternalVO` values equal to this node's old default VO path.
- **The status bar then shows:**
  - `Status_StartNodeMade`: "Node {0} is now node 0, the start node", when there are no
    references;
  - `Status_StartNodeMadeWithReferences`: "…; {1} reference(s) in other conversations still
    name node {0}", when there are some.

### 6. The canvas (Avalonia)

**The warning strip.** A strip under the canvas toolbar in `ConversationView.axaml`, bound
to `ShowStartNodeWarning`. It uses the existing validation brushes
(`Brush.Validation.*`).

| State | Text | Buttons |
|---|---|---|
| Created in the editor | `Canvas_NoStartNode_New`: "This conversation has no node 0, so the game can't start it from its first line." | **Choose start node…** (`Canvas_ChooseStartNode`) |
| Picking | `Canvas_ChooseStartNode_Prompt`: "Click the node that should start the conversation. Esc cancels." | **Cancel** |
| Shipped conversation | `Canvas_NoStartNode_Game`: "Node 0 was removed, so the game can't start this conversation. Undo the deletion, or remove it from the patch." | none |

- Every control has a localised `ToolTip` and a UIA name.
- Esc on the canvas cancels picking.

**The node context menu** gets "Make this the start node" (`Menu_MakeStartNode`, with a
tooltip). It is visible only when `CanChooseStartNode` is true and `HasStartNode` is false.

## Error handling

- File moves (VO) go through `try`/`catch`. On failure the command:
  - logs through `AppLog.Error`;
  - restores any file already moved, with its own failure logged too;
  - leaves the model unchanged;
  - shows `Status_StartNodeVoRenameFailed`.

  A command that fails isn't pushed onto the undo stack.
- A failed precondition is reported in the status bar and never throws.

## Testing

All tests are written first (strict TDD).

**Core:**
- `FlowAnalysisService`, on a conversation without node 0:
  - `HasStartNode` is false;
  - no node is `Unreachable`;
  - the other checks still fire.
  - Replaces `Analyze_NoRootNode_AllNodesUnreachable`.
- `FlowAnalysisService`, empty conversation: `HasStartNode` is true.
- `PathStatsService` without node 0:
  - `HasStartNode` is false;
  - the word totals are still computed.

**ViewModels:**
- `MakeStartNodeCommand` on node *N*:
  - the node's ID is 0;
  - the links to and from it name 0;
  - the snapshot's text, comment and position follow it.
- Undo restores everything, and Redo repeats it.
- The precondition failures (not new, node 0 exists, read-only, VO target exists) change
  nothing and push nothing.
- VO: the staged primary and female files are renamed and renamed back. With `ExternalVO`
  set, nothing is renamed.
- Translations:
  - renumber then save keeps the other languages' text, now under key 0;
  - renumber, save, Undo, save puts it back under *N*.
- `HasStartNode` and `ShowStartNodeWarning` follow adds, deletes, renumbers, Undo and Redo.
- Pick mode:
  - selecting a node renumbers it and ends the mode;
  - Cancel ends it without changing anything;
  - the mode is unavailable for a shipped conversation.
- `CanChooseStartNode` is true for a new conversation and false for a shipped one
  (`MainWindowViewModel` load tests).
- `StartNodeReferenceScanner` counts:
  - an explicit `StartConversation` with the old ID;
  - an `ExternalVO` alias.

  It ignores calls to other conversations or other IDs.
- `FlowAnalyticsViewModel` shows the NoStartNode row, and Path Stats shows its message.

**End to end:** a new conversation that starts at node 1, as in a pre-issue-162 project, is
fixed with the command, saved and installed through `PatchInstaller`. The installed file has
a node 0 that the links point to.

**GUI:** after the build, a `DriveApp.ps1` check that:
- the strip is visible;
- "Choose start node…" can be found by its UIA name;
- the context-menu item exists.

## Risks

- **`NodeId` stops being immutable.** Code that caches a node's ID could go stale. The
  setter is internal and only this command uses it. The plan lists the places that cache IDs
  (selection history, Find results, and the open analytics windows, which refresh on edit)
  and checks each one.
- **Translations carried across saves.** The remap log is the only thing standing between a
  renumber and lost translations, so its tests cover save → Undo → save.
- **The file system and undo.** Renaming VO files is a side effect outside the model. It is
  done only inside the project's `_vo/` folder, and it is reverted on failure.
