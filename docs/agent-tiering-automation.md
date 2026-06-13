# Tiered Agent Automation — Implementation Plan

> Working notes for moving from manual, single-model work to a tiered
> (Opus / Sonnet / Haiku) agent workflow, and ultimately to a largely
> autonomous pipeline. Captures the role mapping, the per-task dispatch
> checklist, and the staged automation path. Implement incrementally —
> **Stage 2 (hooks) is the prerequisite for trusting Stage 3 (the loop).**

---

## 0. Core principle

Match **cognitive load to capability cost**. Each tier has a different
cost / latency / reasoning curve. Tiering only pays off when the dispatcher
(the smart model) can decompose work into pieces whose difficulty is
*predictable* — because the failure mode of under-powering a task is **silent**:
a weak model produces plausible-but-wrong output, which is more expensive than
just using the strong model.

**One-sentence rule:** *delegate only what's verifiable, never delegate
judgment, hand off at the test boundary.*

---

## 1. Role mapping — brain / hands / reflexes

### Opus — the orchestrator & reasoner
- Decomposing an ambiguous request into a plan (brainstorming → writing-plans)
- Architecture decisions, trade-off analysis, API/contract design
- Reviewing subtle correctness (race conditions, the `AppSettings`/`Loc`
  global-state issues, TDD red/green discipline)
- Synthesizing subagent results and deciding what to do next
- Anything where being **wrong** is expensive and being **slow** is acceptable

### Sonnet — the implementer
- Executing a *well-specified* task from a plan Opus wrote
- Writing code/tests where the design is already decided
- Bounded edits with clear acceptance criteria
- The bulk of the actual file-editing labor

### Haiku — the mechanical worker / reflex layer
- Classification & routing ("which files mention `Palette.Line.*`?")
- Extraction, reformatting, mechanical renames
- Summarizing tool output, triaging logs
- High-volume parallel fan-out where each unit is trivial and verifiable

---

## 2. Per-task dispatch checklist

### Step 1 — Gate: is this even tierable?
Any "no" → **Opus does it inline, stop.**
- [ ] Can I state the task in one sentence with a **checkable** done-condition?
      (a named test passes, the diff compiles, output matches a golden file)
- [ ] Is the **design already decided**? (no open architecture/trade-off questions)
- [ ] Is the spec **self-contained**? (a cold-start agent could do it without
      conversation history)
- [ ] Does the work **exceed the dispatch overhead**?
      (bigger than ~1 file / a few minutes of inline work)

### Step 2 — Tier selection (first match wins)
```
Is the task PLANNING, REVIEW, or AMBIGUOUS?
  └─ yes → OPUS (do not delegate judgment)

Is the output VERBATIM-CHECKABLE with ZERO reasoning?
(grep sweeps, rename, reformat, extract, summarize, list rule violations)
  └─ yes → HAIKU

Otherwise (bounded implementation from a clear spec):
  └─ SONNET
```
> The middle branch is the "100% certain it's trivial" rule made concrete:
> **if producing the output requires *deciding* anything, it's not Haiku.**
> Listing windows missing `Icon=` is Haiku. Deciding *what* icon path is
> correct is not.

### Step 3 — TDD mapping (hand off at the test boundary)
| Phase | Tier | Why |
|-------|------|-----|
| Decide behavior + write the failing test | **Opus** | The test *is* the spec; wrong test poisons everything downstream |
| Make the failing test pass (green) | **Sonnet** | Spec locked; success mechanically defined |
| Refactor under green | **Sonnet** | Bounded, with a safety net |
| Review: was TDD followed? subtle correctness? | **Opus** | Catches "impl first", global-state races, rule violations |
| Mechanical sweeps feeding the review | **Haiku** | "list every new `<Window>`", "find hard-coded strings" |

> **Never delegate test-writing.** A wrong test passed by a weak model is the
> silent-failure trap.

### Step 4 — Per-handoff spec template
Every delegation to Sonnet/Haiku must carry:
- [ ] **Task** (one sentence)
- [ ] **Done-condition** (the exact command/test that proves success)
- [ ] **Constraints** (localisation; mandatory tooltips; `Icon=` on windows;
      `AppLog.Error/Warn` on catches; tests run serially; TDD order)
- [ ] **Files in scope** (and explicitly out of scope)
- [ ] **Return** (the diff + the test output, so Opus can verify without re-running)

### Step 5 — Integration gate (Opus, always)
- [ ] Test output actually shows green (not just "agent says done")
- [ ] TDD order respected (test predates impl)
- [ ] No project-rule regressions (tooltips, localisation, error logging)
- [ ] Diff matches the spec's scope — nothing extra

---

## 3. Path to full automation

> **Key reframe:** you can't automate away the *dispatcher* (tier selection is
> itself reasoning), but you can automate away the *human*. Today the human is
> the safety net catching a weak model's silent-wrong output. Remove the human
> and the **automated gate becomes the only thing** between a plausible-but-wrong
> result and a merged bug. So the path to automation **is** the path to
> bulletproof, deterministic verification.

### Stage 0 — Codify the checklist *(done)*
Section 2 lives as `.claude/skills/dispatching-tiered-agents/SKILL.md`, so the
orchestrator applies it identically every session.

### Stage 1 — Make dispatch a tool call, not a manual model switch *(done)*
Purpose-built subagents with pinned `model:` frontmatter live in `.claude/agents/`:
- `opus-orchestrator` — planning, review, integration
- `sonnet-implementer` — green-the-test work; system prompt carries the Step 4
  spec template
- `haiku-sweeper` — grep / rename / extract / list-violations

"Delegate to Sonnet" is now `Agent(subagent_type: sonnet-implementer, …)` —
deterministic and repeatable. (New project agents register on session
restart.)

### Stage 2 — Move the gates from judgment into hooks *(KEYSTONE, not started)*
Hooks in `settings.json` run **deterministic code, not LLM reasoning** — exactly
right for the verification layer automation makes load-bearing. This repo's rules
are already mechanical. Split into two passes — the first de-risks the easy 80%
before tackling the two genuinely hard process gates.

**Pass A — mechanical PostToolUse checks (≈ a day, dominated by tuning)**
- a new `<Window>` missing `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`
  *(S — single regex check on `.axaml`, low ambiguity)*
- a `catch` block without `AppLog.Error/Warn` (and not a bare `catch {}`)
  *(S–M — brace-matching on C# catch blocks is fragile but workable; the
  `OperationCanceledException` exception is simple)*
- hard-coded user-visible strings in `.axaml` / `.cs` (localisation rule)
  *(L — the check itself is easy; the heuristic for "user-visible" vs.
  bindings/`x:Name`/enum values/XML namespaces/paths needs several rounds of
  tuning against real false positives in this codebase)*

**Pass B — process gates (≈ half a day each, separate design problem)**
- **Stop hook**: fails if tests didn't run green *(M — needs a way to know
  tests* actually *ran, not just "agent says so"; likely a state file written
  by a wrapper around `dotnet test`, plus a decision on full-suite re-run vs.
  freshness check)*
- **TDD-order enforcement**: reject an implementation edit when no
  corresponding failing test was added first *(L — hardest; not visible to a
  single PreToolUse hook, needs git-diff-based ordering or a session state
  file tracking test-run history)*

These catch the silent-failure mode cheaper tiers introduce — mechanically,
without trusting any model's self-report.
> Build candidates: draft as **hookify** rules and/or raw `settings.json` hooks.
> Revisit Pass B's shape once Pass A has been run for a while — what's noisy
> /useful in practice may change the design.

### Stage 3 — Close the loop with an unattended orchestrator
An orchestrator skill drives **plan → dispatch → verify → integrate**, wrapped in
`/loop` (self-paced) or `CronCreate` (scheduled) for hands-off runs. Human sets
the goal; the Stage-2 hooks are guardrails the loop can't talk past.
> **Do not run the loop until the gates are airtight** — otherwise you just
> generate wrong code faster.

### Stage 4 — Feedback so routing improves
When a delegated task fails verification, capture *why* to memory
("Haiku mis-handled X-type tasks → route to Sonnet"). Over time the dispatcher's
difficulty predictions calibrate to this codebase instead of generic priors.

---

## 4. Benefits vs. drawbacks

**Benefits**
- Cost & latency: push volume work down-tier (Haiku ≈ 10–20× cheaper than Opus)
- Throughput via parallel fan-out on cheap tiers
- Forced decomposition improves quality even ignoring cost — a task you can't
  hand off cleanly is one you don't yet understand
- Context hygiene: subagents keep grunt-work output out of the orchestrator's window

**Drawbacks / risks**
- **Silent under-powering** — confident-but-wrong output; mitigate by requiring a
  *checkable* result per task
- **Cold-start tax** — every spawned agent re-derives context; for small tasks,
  inline Opus is cheaper end-to-end
- **Coordination overhead** — more handoffs, more places instructions garble
- **Gate is single-point-of-failure** once the human is out of the loop — invest
  disproportionately in Stage 2
- **Cost runaway in loops** — cap iterations / budget in Stage 3
- **Don't let hooks *decide*** — they enforce, they can't reason

---

## 5. Honest end-state

> Opus orchestrates autonomously; Sonnet/Haiku execute; deterministic hooks
> enforce every checkable rule; human reviews only at **integration milestones**,
> not per-task. Fully human-free is achievable for *well-trodden* work
> (palette/token tasks fit); **novel architecture still wants a human at the
> planning gate** — the one step where being wrong is unrecoverable and there is
> no test to catch it.

**Sequencing:** Stage 0 → 1 → **2 (build first / highest leverage)** → 3 → 4.

---

## 6. Suggested first build
Draft Stage 2 hooks from rules already written down in `CLAUDE.md`:
localisation, mandatory tooltips, `Icon=` on every `<Window>`,
`AppLog.Error/Warn` on every catch, serial tests, TDD order. Highest leverage
regardless of how far the rest is taken.
