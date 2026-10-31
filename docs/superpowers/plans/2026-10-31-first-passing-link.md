# First-passing-link rule (link shadowing) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Path stats, Flow Analytics and the canvas follow the game's rule that the first link which passes for
certain hides the links after it (#353).

**Architecture:** One pure Core helper, `LinkShadowing`, takes a source node's links in order (verdict + target
kind per link) and returns, per link, the index of the link that hides it. `PathStatsService.LinksOf`,
`FlowAnalysisService` (warning + reachability) and `ConversationViewModel` (canvas fade, computed once per
source node) all call it. Two new read-only node fields, `PlayType` and `IsQuestionNode`, feed it.

**Tech Stack:** .NET 10, Avalonia, CommunityToolkit.Mvvm, Nodify, xUnit (+ Avalonia.Headless).

**Spec:** `docs/superpowers/specs/2026-10-31-first-passing-link-design.md` (read it first, especially §3 and §7a).

## Global Constraints

- Strict red/green TDD (CLAUDE.md): a failing test first, then the minimum code.
- No user-visible string inline in C# or XAML: strings go in `DialogEditor.Avalonia/Resources/Strings.axaml`
  (the only locale file), read via `Loc.Get` / `Loc.Format` / `Loc.FormatCount` / `{DynamicResource}`.
- Every interactive control has a detailed `ToolTip` and a UIA name (none are planned).
- Source files carry a UTF-8 BOM; keep it. New .cs files: match the BOM of neighbouring files.
- Tests headless only: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`. Never bare `dotnet test`.
- Game ids are `"poe1"` / `"poe2"`.
- Hide only behind a link that is `True` for certain; `Unknown`, `Opaque`, once-only targets and non-Normal
  play types hide nothing. Figures may stay upper bounds, never under-count.
- The Flow Analytics warning and `Unreachable` use an **empty** `GameState` (never the assumed state).
- `CHANGELOG.md` is frozen: do not touch it.

## Review Focus

1. A Random/Cycle source node with an unconditional first link: nothing hidden (Task 2 `NonNormalPlayType_HidesNothing`).
2. A node loaded from a patched project or another conversation keeps `PlayType`/`IsQuestionNode`/`ScriptKind`,
   so a bank or question target is still `Opaque` there (Task 1 `SnapshotBuilder_CarriesAnalysisFields`).
3. Undoing a link deletion puts the link back at its original index, so hiding doesn't flip after undo
   (Task 5 `UndoDeleteLink_RestoresLinkOrder`).
4. An Unknown player response before a certain NPC link keeps later responses live (Task 2
   `MaybeResponseBeforeCertainNpc_KeepsLaterResponses`).
5. With no game state, a conversation without hiding links gives byte-identical path stats to before
   (Task 3 `NoHidingLinks_FiguresUnchanged`).

---

### Task 1: `PlayType` and `IsQuestionNode` node fields

**Files:**
- Modify: `DialogEditor.Core/Models/NodeEnumNames.cs` (add `PlayTypes`), `DialogEditor.Core/Parsing/Poe2EnumMaps.cs`
  (add `PlayTypeName`), `DialogEditor.Core/Models/ConversationNode.cs`, `DialogEditor.Core/Editing/ConversationEditSnapshot.cs`,
  `DialogEditor.Core/Parsing/Poe1ConversationParser.cs`, `DialogEditor.Core/Parsing/Poe2ConversationParser.cs`,
  `DialogEditor.Patch/ConversationSnapshotBuilder.cs` (`BuildNode` and `ToConversation`),
  `DialogEditor.ViewModels/ViewModels/NodeViewModel.cs` (ctor + `ToSnapshot`).
- Test: parser tests for each game under `DialogEditor.Tests/Parsing/`, `DialogEditor.Tests/Patch/ConversationSnapshotBuilderTests.cs`,
  serializer round-trip tests under `DialogEditor.Tests/Serialization/`.

**Interfaces — Produces:**
- `NodeEnumNames.PlayTypes = ["Normal", "Random", "CycleLoop", "CycleStop", "CycleRandom"]` (OEIFormats order).
- `Poe2EnumMaps.PlayTypeName(JsonNode?) -> string` (same pattern as `PersistenceName`: missing → `""`, out of range → `"Unknown(n)"`).
- `ConversationNode(..., ScriptNodeKind ScriptKind = Script, string PlayType = "", bool IsQuestionNode = false)`.
- `NodeEditSnapshot` init props `[JsonIgnore] string PlayType { get; init; } = ""` and `[JsonIgnore] bool IsQuestionNode { get; init; }`.
- `NodeViewModel.PlayType` / `NodeViewModel.IsQuestionNode`: get-only, set from the `ConversationNode` in the ctor, written into `ToSnapshot` next to `ScriptKind`.

- [x] **Step 1: Failing tests.**
  - `Poe1Parser_ReadsPlayTypeAndIsQuestionNode`: XML node with `<PlayType>Random</PlayType><IsQuestionNode>true</IsQuestionNode>` → `"Random"`, `true`; a node without those elements → `""`, `false`.
  - `Poe2Parser_ReadsPlayTypeAndIsQuestionNode`: JSON `"PlayType": 4, "IsQuestionNode": true` → `"CycleRandom"`, `true`; missing → `""`, `false`.
  - `SnapshotBuilder_CarriesAnalysisFields`: `ConversationSnapshotBuilder.Build` of a conversation whose node has `ScriptKind = Bank`, `PlayType = "Random"`, `IsQuestionNode = true` keeps all three on the snapshot; `ToConversation` keeps all three back on the `ConversationNode`.
  - `NodeViewModel_ToSnapshot_CarriesPlayTypeAndIsQuestionNode`.
  - Round-trip: a PoE1 and a PoE2 file whose node has a non-default `PlayType` / `IsQuestionNode` serialize back with the same values (the serializers already pass these through: confirm with a test, fix only if it fails).
- [x] **Step 2: Run, see them fail.** `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~Parser|FullyQualifiedName~SnapshotBuilder|FullyQualifiedName~NodeViewModel"` → the new tests fail to compile or fail.
- [x] **Step 3: Implement** the fields as listed under Interfaces. PoE1: `(string?)node.Element("PlayType") ?? ""`, `(bool?)node.Element("IsQuestionNode") ?? false`. `ConversationSnapshotBuilder.BuildNode` also sets `ScriptKind` (it drops it today).
- [x] **Step 4: Run the full headless suite.** Expected: all green.
- [x] **Step 5: Commit** `feat: read PlayType and IsQuestionNode for analysis (#353)`.

### Task 2: `LinkShadowing` (Core)

**Files:** Create `DialogEditor.Core/Conditions/LinkShadowing.cs`, `DialogEditor.Tests/Conditions/LinkShadowingTests.cs`.

**Interfaces — Consumes:** Task 1 fields; `LinkFiring.CannotFire`, `ConditionEvaluator.Evaluate`, `Truth`, `GameState`.

**Interfaces — Produces:**
```csharp
public enum LinkTargetKind { Npc, Response, Opaque }
public readonly record struct ShadowLink(Truth Verdict, LinkTargetKind Kind);   // Kind ignored when Dangling
public sealed record ShadowTarget(bool IsPlayerChoice, ScriptNodeKind ScriptKind, bool IsQuestionNode,
                                  string Persistence, IReadOnlyList<ConditionNode> Conditions)
{ public static ShadowTarget From(NodeEditSnapshot n); }

public static class LinkShadowing
{
    // Per link index: the index of the link that hides it, or null.
    public static IReadOnlyList<int?> HiddenBy(string sourcePlayType, IReadOnlyList<ShadowLink?> links);
    // null target = dangling link → null entry in the list passed to HiddenBy.
    public static ShadowLink? Describe(IReadOnlyList<ConditionNode> linkConditions, ShadowTarget? target,
                                       GameState state, string gameId);
    // Convenience for snapshot callers: Describe every link of `source`, then HiddenBy.
    public static IReadOnlyList<int?> HiddenBy(NodeEditSnapshot source, Func<int, NodeEditSnapshot?> nodeById,
                                               GameState state, string gameId);
}
```
`Describe`: Kind = `Opaque` if `ScriptKind == Bank` or `IsQuestionNode`, else `Response` if `IsPlayerChoice`, else `Npc`.
Verdict = `False` if `LinkFiring.CannotFire(...)`; `True` if Kind ≠ Opaque, both conditions evaluate `True`, and
`Persistence` is `""`, `"None"` or `"MarkAsRead"`; else `Unknown`. `HiddenBy` implements spec §3 exactly (null
entries neither hide nor are hidden; `False` entries are skipped; only the first `True` link hides; non-`Normal`
and non-empty play type → all null).

- [x] **Step 1: Failing tests** (`HiddenBy` with hand-built `ShadowLink`s unless noted; `T/U/F` = verdict, `N/R/O` = kind):
  - `CertainNpcFirst_HidesAllLater`: `[T N, U N, U R]` → `[null, 0, 0]`.
  - `CertainResponseFirst_HidesLaterNpcOnly`: `[T R, U N, U R, T N]` → `[null, 0, null, 0]`.
  - `UnknownFirst_HidesNothingBeforeCertain`: `[U N, T N, U N]` → `[null, null, 1]`.
  - `MaybeResponseBeforeCertainNpc_KeepsLaterResponses`: `[U R, T N, U R, U N]` → `[null, null, null, 1]`.
  - `OpaqueCountsAsMaybeResponse`: `[U O, T N, U R]` → `[null, null, null]`.
  - `FalseLinksSkipped`: `[F N, T N, U N]` → `[null, null, 1]`.
  - `DanglingIgnored`: `[null, T N, null, U N]` → `[null, null, null, 1]`.
  - `OnlyFirstCertainHides`: `[T N, T N]` → `[null, 0]`.
  - `NonNormalPlayType_HidesNothing`: play type `"Random"`, `"CycleLoop"`, `"Unknown(9)"` with `[T N, U N]` → all null; `""` and `"Normal"` hide.
  - `Describe_NoConditionsNonePersistence_IsTrue`, `Describe_OncePerConversationTarget_IsUnknown`, `Describe_BankTarget_IsOpaqueUnknown`,
    `Describe_QuestionNodeTarget_IsOpaque`, `Describe_FalseUnderState_IsFalse`, `Describe_ConditionUnknownWithEmptyState_IsUnknown`
    (build leaves as `LinkFiringTests` / `ConditionEvaluatorTests` do).
- [x] **Step 2: Run, see them fail** (`--filter "FullyQualifiedName~LinkShadowingTests"`).
- [x] **Step 3: Implement** `LinkShadowing.cs`, with a class doc comment citing `GetResponseNodes` / `GetNextNode` as in spec §2.
- [x] **Step 4: Run, green.**
- [x] **Step 5: Commit** `feat: LinkShadowing models the first-passing-link rule (#353)`.

### Task 3: Path stats and the Flow Analytics count line

**Files:** `DialogEditor.Core/Analytics/PathStatsService.cs`, `PathStatsModels.cs`,
`DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs`, `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml`,
`Strings.axaml`; tests in `DialogEditor.Tests/Analytics/PathStatsServiceTests.cs` and `.../ViewModels/FlowAnalyticsViewModelTests.cs`.

**Interfaces — Consumes:** `LinkShadowing.HiddenBy(NodeEditSnapshot, Func<int, NodeEditSnapshot?>, GameState, string)`.
**Produces:** `PathStatsReport.ShadowedLinkCount` (init, default 0); VM `HasShadowedLinks`, `ShadowedLinksText`.

- [x] **Step 1: Failing tests.**
  - `ShadowedBranch_LeavesLongestAndShortest`: root → `[A (no conds), B (long text)]` with no state → longest excludes B.
  - `ShadowedChoice_LeavesForkTree`: root → `[NPC no conds, choice C]` → C not in branches.
  - `ShadowedLinkCount_CountsHiddenLinks` (with and without a state).
  - `NoHidingLinks_FiguresUnchanged`: every link conditioned on an unset global → report equals what today's code produces (compare against hard-coded expected figures).
  - `ShadowingUsesAssumedState`: link 0 conditioned on global `g == 1`; with a state setting `g = 1`, link 1 is hidden; without, it isn't.
  - `CrossConversation_HidingAppliesPerConversation`: a target in conversation B is looked up within B (`NodeRef.Conversation`).
  - VM: `ShadowedLinksText` shows `Loc.FormatCount("PathStats_ShadowedLinks", n)` when n > 0 whether or not a state is set; hidden at 0.
- [x] **Step 2: Run, see them fail.**
- [x] **Step 3: Implement.** In `Analyze`, compute per node (memoised per `NodeRef`) the hidden set with
  `LinkShadowing.HiddenBy(node, id => nodeById.GetValueOrDefault(new NodeRef(conv, id)), state ?? new GameState(), gameId)`;
  `LinksOf` drops a link that is blocked **or** hidden. `ShadowedLinkCount` counts hidden links over all nodes.
  Update the class doc comment (one paragraph, #353). Strings: `PathStats_ShadowedLinks_One` = "1 link never fires because an earlier link always passes.",
  `PathStats_ShadowedLinks_Other` = "{0} links never fire because an earlier link always passes."; muted
  TextBlock under `PrunedLinksTextBlock`, same style, `x:Name="ShadowedLinksTextBlock"`.
- [x] **Step 4: Full headless suite green** (including `NoClippedTextTests`).
- [x] **Step 5: Commit** `feat: path stats follow the first-passing-link rule (#353)`.

### Task 4: Flow Analytics warning and reachability

**Files:** `DialogEditor.Core/Analytics/FlowAnalysisModels.cs`, `FlowAnalysisService.cs`,
`DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs` (`FlowIssueViewModel` message), `Strings.axaml`,
plus any exhaustive `FlowIssueKind` switch (`NodeDetailViewModel` line ~644, the severity converters); tests in
`DialogEditor.Tests/Analytics/FlowAnalysisServiceTests.cs`.

**Produces:** `FlowIssueKind.LinkShadowed`; `FlowIssue(int NodeId, FlowIssueKind Kind)` gains init props
`int? RelatedNodeId` (the hidden link's target) and `int? HiderNodeId` (the hiding link's target).

- [x] **Step 1: Failing tests.**
  - `ShadowedLink_ReportedOnSourceNode`: node 0 → `[1 (no conds), 2]` → one `LinkShadowed` on node 0, `RelatedNodeId = 2`, `HiderNodeId = 1`.
  - `NodeReachedOnlyThroughShadowedLink_IsUnreachable`: node 2 above → `Unreachable`.
  - `ConditionedFirstLink_NoWarning`: link to 1 conditioned on an unset global → no `LinkShadowed`, node 2 reachable.
  - `RandomPlayType_NoWarning`.
  - VM message: "Link to node 2 never fires: the link to node 1 before it always passes." (`FlowAnalytics_Issue_LinkShadowed`, format `{0}` = related, `{1}` = hider).
- [x] **Step 2: Run, see them fail.**
- [x] **Step 3: Implement** with `LinkShadowing.HiddenBy(node, nodeById lookup, new GameState(), gameId)`; the BFS skips hidden links (this also changes `MaxDepth`, which is intended). Severity: warning (not `Unreachable`).
- [x] **Step 4: Full headless suite green.**
- [x] **Step 5: Commit** `feat: warn about links hidden by an earlier link (#353)`.

### Task 5: Canvas fades hidden links

**Files:** `DialogEditor.ViewModels/ViewModels/ConnectionViewModel.cs`, `ConversationViewModel.cs`,
`DialogEditor.Avalonia/Views/ConversationView.axaml`, `LegendWindow.axaml` (help text only), `Strings.axaml`;
tests in `DialogEditor.Tests/ViewModels/`.

**Produces:**
- `ConnectionViewModel.ShadowedByNodeId` (`int?`, set by the canvas), `IsShadowed => ShadowedByNodeId is not null`,
  `IsFaded => CannotFire || IsShadowed`, `FadedToolTip => CannotFireToolTip ?? (IsShadowed ? Loc.Format("ToolTip_LinkShadowed", ShadowedByNodeId) : null)`.
- `ConversationViewModel.RefreshShadowing(NodeViewModel source)` and `RefreshAllShadowing()`.

- [x] **Step 1: Failing tests.**
  - `UnconditionalFirstLink_FadesLaterLinks` (no state set).
  - `AddingConditionToFirstLink_UnfadesLaterLinks` (edit the connection's conditions).
  - `TargetPersistenceOncePerConversation_UnfadesLaterLinks` (edit target node's `Persistence`).
  - `AssumedStateMakesFirstLinkCertain_FadesLaterLinks` (state change event).
  - `NewLinkAfterCertainLink_IsFadedOnAdd`; `DeletingTheHider_UnfadesTheRest`.
  - `UndoDeleteLink_RestoresLinkOrder`: delete the first of three links, undo → `BuildSnapshot()` link order is the original; fix the undo command to reinsert at the original index if it fails.
  - `CannotFireTooltipWinsOverShadowed`.
- [x] **Step 2: Run, see them fail.**
- [x] **Step 3: Implement.** Link order for a source = order in `Connections` filtered by `Source.Owner` (as in
  `BuildSnapshot`). Refresh: a source on `Connections` add/remove (Reset → all); all on `OnAssumedStateChanged`
  and `ApplyVerdictContext`; on a node's `Conditions` / `Persistence` / `IsPlayerChoice` change, every source with
  a link into it; on a connection's `Conditions` change, its source. Verdict state = assumed state if it has a fact, else `new GameState()`.
  XAML: bind `Classes.cannotFire="{Binding IsFaded}"` and `ToolTip.Tip="{Binding FadedToolTip}"` (one style, spec §6).
  Strings: `ToolTip_LinkShadowed` = "This link never fires: the link to node {0} comes before it and always passes. The game takes the first link that passes, in link order.";
  extend `Legend_CannotFire_Help` with a sentence: "A connection also fades when an earlier link from the same line always passes, because the game takes the first link that passes."
  and `Legend_CannotFire_Desc` to " — ruled out by the game state or an earlier link".
- [x] **Step 4: Full headless suite green.**
- [x] **Step 5: Commit** `feat: canvas fades links hidden by an earlier link (#353)`.

### Task 6: Verification, docs, PR (orchestrator)

- [x] Headless suite green; `trail check` clean.
- [x] Run the app (`running-the-app`) on a PoE2 conversation with an unconditional first link: faded later link with
      the tooltip, the warning in Flow Analytics, the shadowed-count line; screenshots.
- [x] Spec/plan describe the code as built; `## Handoff` appended; `trail note` for the game-code findings.
- [x] Search then file the follow-up issue: a command to reorder a node's links.
- [x] Open the PR referencing #353; enable Auto-fix.

## Handoff — 2026-10-31

**Done** (branch `feat/353-first-passing-link`; the PR closes #353):
- Spec and plan (`8f5b1850`, `76ac8e78`), plus a spec amendment (`0ee99a08`): a certain NPC link keeps later
  opaque links when a response may pass. Spec brought up to date with the build (`5ec71d84`).
- Task 1 (`465ca070`): the node fields `PlayType` / `IsQuestionNode`. `ConversationSnapshotBuilder` now
  carries `ScriptKind` / `PlayType` / `IsQuestionNode` in both directions (it used to drop `ScriptKind`).
- Task 2 (`58c65508`, `fcad21b1`): `LinkShadowing` (`Describe`, `HiddenBy`).
- Task 3 (`34b32764`): path stats skip hidden links. Adds `ShadowedLinkCount` and the Flow Analytics count line.
- Task 4 (`8492fa25`): the `FlowIssueKind.LinkShadowed` warning. `Unreachable` now skips hidden links (both use
  an empty state).
- Task 5 (`04810c2f`, `9cb96677`): the canvas fades hidden links (`IsFaded` / `FadedToolTip`). Undo of link
  deletion and of node deletion now reinserts connections at their original index. Loading refreshes the hiding once.
- Final-review fixes (`a5d6c288`): bank *sources* hide nothing; the tooltip and path-stats line say "sure to pass".
- Trail learning: game-code findings (`edfeb805`). Follow-up issue: #359 (reorder a node's links).
- Headless suite green (5164 passed). GUI check: on a copy of the canonical PoE2 fixture, with an unconditional
  NPC link inserted first on node 0, the canvas faded 0→1, 0→2 and 0→3 and drew 0→7 at full strength. Flow
  Analytics was not opened in the GUI; headless tests cover the warning text and the count line against the real
  resources.

**Remaining:** review and merge the PR.

**Next step:** review the _(my call)_ items in the spec. The most visible is that path stats now change with no
game state for conversations that contain a link that is sure to pass. In shipped PoE2 data, 390 nodes hide
802 links.

**Open questions:** none blocking. Possible follow-up: Flow Analytics' issue-kind column is 110px wide and cuts
the new warning text short. The node numbers then show only in the tooltip, as with other long issue labels.

**Do not redo:** reading `PlayType` from a bank *source* (a bank has none; the game uses the child that just
played). Treating question nodes as certain targets (on a revisit they expand to responses).
