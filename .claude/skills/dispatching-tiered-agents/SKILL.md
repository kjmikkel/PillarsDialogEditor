---
name: dispatching-tiered-agents
description: Use when deciding whether to handle a task inline or delegate it to a subagent, and which model tier (Opus/Sonnet/Haiku) to dispatch to, in the Dialog Editor project.
---

# Dispatching Tiered Agents

## Core principle
Match cognitive load to capability cost. Delegate only what's verifiable, never
delegate judgment, hand off at the test boundary. A weak model producing
plausible-but-wrong output is *more* expensive than doing it yourself — that
failure mode is silent.

## Step 1 — Gate: is this even tierable?
Any "no" → do it inline, stop.
- Can the task be stated in one sentence with a **checkable** done-condition
  (a named test passes, the diff compiles, output matches a golden file)?
- Is the **design already decided** (no open architecture/trade-off questions)?
- Is the spec **self-contained** (a cold-start agent could do it without this
  conversation's history)?
- Does the work **exceed dispatch overhead** (more than ~1 file / a few
  minutes of inline work)?

> A TDD "make the failing test green" handoff (Step 3) still goes to
> `sonnet-implementer` even for a single-file/single-line change — the gate
> above is about whether *you'd* spend real effort on it, not file count.

## Step 2 — Tier selection (first match wins)
- Planning, review, or ambiguous? → **Opus** (do not delegate judgment) —
  dispatch to `opus-orchestrator`
- Output verbatim-checkable with **zero reasoning** (grep sweeps, rename,
  reformat, extract, summarize, list rule violations)? → **Haiku** — dispatch
  to `haiku-sweeper`
- Otherwise (bounded implementation from a clear spec) → **Sonnet** —
  dispatch to `sonnet-implementer`

If producing the output requires *deciding* anything, it's not Haiku.

## Step 3 — TDD handoff boundary
| Phase | Tier |
|---|---|
| Decide behavior + write the failing test | Opus |
| Make the failing test pass (green) | Sonnet |
| Refactor under green | Sonnet |
| Review: TDD followed? subtle correctness? | Opus |
| Mechanical sweeps feeding the review | Haiku |

Never delegate test-writing for new behavior.

## Step 4 — Handoff spec (every delegation)
- **Task** (one sentence)
- **Done-condition** (the exact command/test that proves success)
- **Constraints** (localisation; mandatory tooltips; `Icon=` on windows;
  `AppLog.Error/Warn` on catches; tests run serially; TDD order)
- **Files in scope** (and explicitly out of scope)
- **Return**: the diff + the test output, so it can be verified without
  re-running

## Step 5 — Integration gate (always, by you)
- Test output actually shows green (not just "agent says done")
- TDD order respected (test predates implementation)
- No project-rule regressions (tooltips, localisation, error logging)
- Diff matches the spec's scope — nothing extra

See `docs/agent-tiering-automation.md` for the full rationale and the staged
automation roadmap (hooks, autonomous loop).
