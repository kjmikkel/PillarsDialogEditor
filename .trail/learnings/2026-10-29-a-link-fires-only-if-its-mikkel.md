---
title: "A link fires only if its own conditions AND its target node's conditions pass; PoE1 checks only the node"
author: Mikkel
date: 2026-10-29
branch: feat/32-condition-aware-path-stats
cost: { minutes: 5, tokens: 140583 }
id: l-b16ab1b8
scope: repo
status: active
touches:
  - docs/superpowers/plans/2026-10-29-condition-aware-path-stats.md
  - docs/superpowers/specs/2026-10-29-condition-aware-path-stats-design.md
---

## Decided
GitHub issue 32. In the decompiled games, PoE2 `FlowChart.GetNextNode` and `Conversation.GetPlayerResponses` follow a link only when `PassesConditionals(link)` and `PassesConditionals(targetNode)` both hold. PoE1's `FlowChartLink` has no conditionals, so PoE1 checks only the target node; `Poe1ConversationParser` already gives every PoE1 link an empty condition list, so the PoE2 rule gives the right answer for both games. `DialogEditor.Core/Conditions/LinkFiring.CannotFire` encodes this once for path stats and the canvas. It prunes only on `Truth.False` and stays quiet while `GameState.HasAnyFact` is false, so figures do not change for writers who never set a fact.

## Watch out
The game also takes the FIRST NPC link that passes (`GetNextNode` returns on the first match), so a link that definitely passes hides later NPC links. Path stats do not model this, with or without an assumed state; spec `docs/superpowers/specs/2026-10-29-condition-aware-path-stats-design.md` §2 lists it as a follow-up. Node persistence (`PassesPersistence`, OnceEver) is not modelled either.
