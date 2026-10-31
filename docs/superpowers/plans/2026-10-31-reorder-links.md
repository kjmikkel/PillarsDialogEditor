# Reorder a node's links — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Writers move a link earlier or later from its right-click menu on the canvas, see link positions on
the selected node, and the order survives save, reopen, export and merging (#359).

**Architecture:** Order lives in `ConversationViewModel.Connections` (per source, as `BuildSnapshot` saves it).
A `MoveLinkCommand` swaps two connections there. Project patches gain `NodeModification.UpdatedLinkOrder`
(schema v4): `DiffEngine` writes it when the order differs from what the rest of the patch gives, and
`PatchApplier` sorts by it. Merging, conflict detection and the git merge treat it like `UpdatedConditions`.

**Tech Stack:** .NET 10, Avalonia, Nodify, CommunityToolkit.Mvvm, System.Text.Json, xUnit (+ Avalonia.Headless).

**Spec:** `docs/superpowers/specs/2026-10-31-reorder-links-design.md` (read it first).

**Branch:** `feat/359-reorder-links`, stacked on `feat/353-first-passing-link` (PR #360). Rebase onto main once #360 merges.

## Global Constraints

- Strict red/green TDD; headless tests only: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`.
- No user-visible string inline: `DialogEditor.Avalonia/Resources/Strings.axaml`, via `Loc` / `{DynamicResource}`.
- Every interactive control has a detailed tooltip and a UIA name (menu items: name from header; `AutomationProperties.HelpText` = tooltip).
- In production code comments write "issue 359", never "#359" (NoStrayHexTests).
- Keep each file's UTF-8 BOM state as it is.
- A patch with no `UpdatedLinkOrder` applies exactly as before; a link is never dropped by ordering.
- `CHANGELOG.md` is frozen.

## Review Focus

1. A project saved before this change (schema 3) loads and applies byte-identically (Task 1 `V3Patch_AppliesUnchanged`).
2. An order record naming a link that no longer exists, or missing a link another patch added: no link lost (Task 1 `Apply_OrderWithUnknownAndMissingIds_KeepsEveryLink`).
3. Reorder then reopen the project: the order is still there (Task 1 `Diff_ThenApply_RoundTripsOrder`).
4. Move then undo then redo leaves the exact original / moved order, with hiding refreshed (Task 3).
5. A node whose only change is its link order is not dropped from diffs, merges or apply-from-diff (Tasks 1–2).

---

### Task 1: Patch format — `UpdatedLinkOrder`, diff, apply, schema v4

**Files:** `DialogEditor.Patch/ConversationPatch.cs`, `DiffEngine.cs`, `PatchApplier.cs`,
`Schema/SchemaMigrator.cs`, create `Schema/Steps/ConversationPatchV3ToV4.cs`; tests in
`DialogEditor.Tests/Patch/` (`DiffEngineTests`, `PatchApplierTests`, schema migrator tests, patch serializer round-trip).

**Produces:** `NodeModification.UpdatedLinkOrder` — `IReadOnlyList<int>? { get; init; }`, target node ids in order;
`ConversationPatch.CurrentSchemaVersion = 4`.

- [ ] **Step 1: Failing tests.**
  - `Diff_ReorderOnly_WritesUpdatedLinkOrder`: base node 0 links [1,2,3], current [3,1,2] → one `NodeModification` for 0 with `UpdatedLinkOrder = [3,1,2]` and no other changes.
  - `Diff_SameOrder_NoLinkOrder`: identical order → no modification.
  - `Diff_AddedLinkAppended_NoLinkOrder`: base [1,2], current [1,2,4] → `AddedLinks=[4]`, `UpdatedLinkOrder == null`.
  - `Diff_AddedLinkFirst_WritesOrder`: base [1,2], current [4,1,2] → `UpdatedLinkOrder = [4,1,2]`.
  - `Diff_DeletedLink_OrderOfRestUnchanged_NoLinkOrder`: base [1,2,3], current [1,3] → no order record.
  - `Apply_UpdatedLinkOrder_SortsLinks`; `Apply_OrderWithUnknownAndMissingIds_KeepsEveryLink`: base [1,2,3], order [3,9,1] → [3,1,2].
  - `Diff_ThenApply_RoundTripsOrder` for several shapes above (apply(base, diff(base, current)) has current's order).
  - `V3Patch_AppliesUnchanged` / migrator: a v3 JSON patch migrates to v4 with no other change; a v4 patch with `UpdatedLinkOrder` round-trips through `PatchSerializer`.
- [ ] **Step 2: Run, see them fail.**
- [ ] **Step 3: Implement.** Diff: compute the applier's order without a record (base links minus deleted in base order, then added in current order); if `current` order differs, set the record to current's order, and count it in the "no change" check. Applier: stable sort by index in the record; unlisted links after listed ones in their existing relative order. `ConversationPatchV3ToV4`: no-op `Apply`, doc comment saying v4 adds the optional `UpdatedLinkOrder` and why the version gate matters (an older editor would drop it silently). Register it.
- [ ] **Step 4: Full headless suite green** (existing schema tests may pin "3": update to the constant).
- [ ] **Step 5: Commit** `feat: project patches record link order (#359)`.

### Task 2: Merging, conflicts, apply-from-diff, Diff view

**Files:** `DialogEditor.Patch/PatchMerger.cs`, `ConflictDetector.cs`, `GitConflict/MergeBuilder.cs`,
`Diff/NodeApplyBuilder.cs`, plus every other site that rebuilds a `NodeModification` (grep `new NodeModification(`
in non-test code and carry `UpdatedLinkOrder` through each), and the Diff view's per-node change summary;
tests alongside existing ones.

**Consumes:** `NodeModification.UpdatedLinkOrder` (Task 1).

- [ ] **Step 1: Failing tests.**
  - `PatchMerger_LaterLinkOrderWins` (and earlier kept when later has none).
  - `ConflictDetector_BothReorderSameNode_ReportsLinkOrderConflict` (field name `"LinkOrder"`); one project reordering → no conflict.
  - `MergeBuilder_PickTheirsLinkOrder` / mine; rebuilding for another field keeps `UpdatedLinkOrder`.
  - Apply-from-diff of a reorder-only node carries the order.
  - Diff view: a reorder-only node is listed as changed, with a "Links reordered" line (string `Diff_LinksReordered` = "Links reordered").
- [ ] **Step 2: Run, see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Full headless suite green.**
- [ ] **Step 5: Commit** `feat: link order survives merging and git conflicts (#359)`.

### Task 3: Move Link Earlier / Later on the canvas

**Files:** create `DialogEditor.ViewModels/Editing/MoveLinkCommand.cs`; `ConversationViewModel.cs`,
`DialogEditor.Avalonia/Views/ConversationView.axaml` (connection context menu), `Strings.axaml`; tests in
`DialogEditor.Tests/ViewModels/` and a game-file save test per game.

**Produces:** `ConversationViewModel.MoveLinkEarlierCommand` / `MoveLinkLaterCommand` (`[RelayCommand]` with
`ConnectionViewModel?` parameter and `CanExecute`), `ConversationViewModel.CanMoveLinkEarlier(ConnectionViewModel?)` /
`CanMoveLinkLater(…)`.

- [ ] **Step 1: Failing tests.**
  - `MoveLinkEarlier_SwapsWithPreviousSiblingOnly`: node 0 links [1,2,3] plus node 5's links interleaved in `Connections`; moving 0→3 earlier gives node 0 order [1,3,2]; node 5's links keep their indices.
  - `MoveLinkLater_…` mirror; `CanMove…` false at the ends and for a null parameter.
  - `MoveLink_UndoRedo_RestoresExactOrder` and marks the conversation modified; undo text uses `Undo_MoveLink`.
  - `MoveLink_RefreshesShadowing`: [certain 1, 2] → move 2 earlier → link to 2 no longer faded, link to 1 now hidden.
  - `ReorderedNode_SavesNewOrder_Poe1` / `_Poe2`: build snapshot after a move, serialize with the game serializer, re-parse → new order.
- [ ] **Step 2: Run, see them fail.**
- [ ] **Step 3: Implement.** The command swaps the two connections in `Connections` (`ObservableCollection.Move`) and its undo swaps them back; refresh hiding for the source after each. Menu: two `ctrl:AccessibleMenuItem`s above Delete Connection, AutomationIds `ConnectionMenu_MoveEarlier` / `ConnectionMenu_MoveLater`, bound like Delete with `CommandParameter="{Binding}"`, hidden in reading mode the way the canvas hides other edit items. Strings: `Menu_MoveLinkEarlier` = "Move Link Earlier", `Menu_MoveLinkLater` = "Move Link Later", `ToolTip_MoveLinkEarlier` = "Move this link one place earlier among its line's links. The game takes the first link that passes, in this order, so an earlier link can hide the ones after it.", `ToolTip_MoveLinkLater` = "Move this link one place later among its line's links. The game takes the first link that passes, in this order, so an earlier link can hide the ones after it.", `Undo_MoveLink` = "Move link to node {0}".
- [ ] **Step 4: Full headless suite green.**
- [ ] **Step 5: Commit** `feat: move a link earlier or later from the canvas (#359)`.

### Task 4: Link position numbers on the selected node

**Files:** `ConnectionViewModel.cs`, `ConversationViewModel.cs`, `ConversationView.axaml` (connection template), `Strings.axaml`; tests in `DialogEditor.Tests/ViewModels/`.

**Produces:** `ConnectionViewModel.LinkPosition` (`int`, 1-based), `LinkCount` (`int`), `ShowLinkPosition` (`bool`: source selected and count ≥ 2), `LinkPositionHelpText` (`Loc.Format("Automation_LinkPosition", position, count)` = "Link {0} of {1}").

- [ ] **Step 1: Failing tests.** Positions after load, after add / delete / move / undo; `ShowLinkPosition` follows the source node's `IsSelected` and is false with one link; help text.
- [ ] **Step 2: Run, see them fail.**
- [ ] **Step 3: Implement.** Recompute positions per source wherever #353's `RefreshShadowing` runs (same triggers) plus on a node's `IsSelected` change. XAML: a small non-interactive label near the connection's source end, visible on `ShowLinkPosition`, using an existing small-font and muted-text token; append `LinkPositionHelpText` to the connection's `AutomationProperties.HelpText`. Placement is your call; it must not cover the arrowhead or node cards at default zoom.
- [ ] **Step 4: Full headless suite green** (including `NoClippedTextTests`).
- [ ] **Step 5: Commit** `feat: show link positions on the selected node (#359)`.

### Task 5: Verification, docs, PR (orchestrator)

- [ ] Headless suite green; `trail check`.
- [ ] Run the app on a copy of the canonical PoE2 fixture: select node 0, see 1/2/3; right-click a link → Move Link Earlier; the fade changes; save the project, reopen, order kept; screenshots.
- [ ] Spec/plan match the code; `## Handoff`; `trail note` if anything took digging.
- [ ] Open the PR (base `feat/353-first-passing-link` until #360 merges, then main), closing #359; enable Auto-fix.
