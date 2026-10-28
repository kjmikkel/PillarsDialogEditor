---
title: "PoE1 and PoE2 combine And/Or conditions differently, left to right with no precedence"
author: Mikkel
date: 2026-10-28
branch: feat/305-condition-evaluator
cost: { minutes: 7, tokens: 93041 }
id: l-a1b93d33
scope: repo
status: active
touches:
  - docs/superpowers/plans/2026-10-28-condition-evaluator.md
  - docs/superpowers/specs/2026-10-28-condition-evaluator-design.md
---

## Decided
GitHub issue 305. Each condition component's `Operator` links it to the NEXT component, and both games fold the list left to right with short-circuits, not with AND-before-OR precedence. The two games' folds differ in the middle components: PoE2 (`ScriptManager.EvaluateExpressionWithOwnerObjects`) decides on the component's own next-operator, PoE1 (`FlowChart.EvaluateExpression`) on the previous operator, then the next one. Example: `A=false Or B=true And C=false` is true in PoE1 and false in PoE2. The evaluator in `DialogEditor.Core/Conditions/` transcribes both literally; the spec `docs/superpowers/specs/2026-10-28-condition-evaluator-design.md` §2 has the steps.

## Watch out
`Not` exists only on calls (`ConditionalCall.Not`); the games' `ConditionalExpression` has no `Not`, so a `ConditionBranch.Not` in the editor model is ignored by the game. Missing globals in PoE2 `GetVariable` return -1 (Catch) or throw (Rethrow, used by `IsGlobalValue`), so an unset global is "can't tell" in the editor, not 0.
