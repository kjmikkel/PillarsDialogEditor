---
title: "Both games pick the next line by the FIRST passing link in order, and that link's kind decides NPC vs response mode"
author: Mikkel
date: 2026-10-31
branch: feat/353-first-passing-link
cost: { minutes: 43, tokens: 421708 }
id: l-d317da82
scope: repo
status: active
touches:
  - DialogEditor.Avalonia/Resources/Strings.axaml
  - DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml
  - DialogEditor.Core/Analytics/PathStatsModels.cs
  - DialogEditor.Core/Analytics/PathStatsService.cs
  - DialogEditor.Tests/Analytics/PathStatsServiceTests.cs
  - DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs
  - DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs
  - DialogEditor.Core/Conditions/LinkShadowing.cs
  - DialogEditor.Tests/Conditions/LinkShadowingTests.cs
  - docs/superpowers/specs/2026-10-31-first-passing-link-design.md
---

## Decided
GitHub issue 353. The UI calls `Conversation.GetResponseNodes` before `GetNextNode` (PoE2 `Game.UI/UIBaseConversationWindow.cs` AdvancePlayerResponse; same in PoE1). `GetResponseNodes` walks the node's links in order: if the first passing link targets an NPC/script node it returns an empty list (later player responses are NOT shown); once a response passes, later non-response links are skipped and later passing responses collected. `GetNextNode` (source PlayType Normal) returns the first passing non-response link. So one rule covers both: the first passing link decides the mode.
The editor models this in `DialogEditor.Core/Conditions/LinkShadowing.cs`; only a link that passes for certain hides anything (no conditions or True ones, target Persistence None/MarkAsRead/empty, target not a bank or question node, source PlayType Normal or empty).
## Watch out
- Not first-match, so they never hide: source PlayType Random/CycleRandom/CycleLoop/CycleStop; bank targets (own play rules, children may be responses); question nodes (hidden text expands to responses on revisit); OnceEver/OncePerConversation targets (`PassesPersistence` fails once played).
- A certain NPC link after a maybe-passing response (or bank/question link) does not hide later responses/opaque links: if the earlier one passes, the game is in response mode.
- Shipped PoE2 data: 390 nodes with an unconditional first-match hide 802 later links with no game state, so path stats changed for real conversations, not just test fixtures.
- `ConversationSnapshotBuilder` used to drop `ScriptKind`; it now carries ScriptKind, PlayType and IsQuestionNode both ways. Any new NodeEditSnapshot field that analysis needs must go through it, or patch-loaded nodes lose it.
