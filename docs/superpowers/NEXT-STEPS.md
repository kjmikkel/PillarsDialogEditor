# Next Steps — pick one at a time

Queued follow-ups from the diff/apply work. Each is self-contained: start a fresh
session, read its entry, and tackle it.

## Completed
- **Diff viewer (Spec 1)** — read-only diff viewer. Shipped on `main`.
- **Selective apply (Spec 2)** — per-node bring-in into the working copy, with applied
  preview, save-guard, single-step undo, count-only dangling warning, and a plain-language
  Help window. Shipped on `main`. Spec/plan: `docs/superpowers/specs/2026-05-30-selective-apply-design.md`,
  `docs/superpowers/plans/2026-05-30-selective-apply.md`.
- **Whole-feature review of the read-only diff viewer** (2026-05-31) — done. Outcome:
  localized endpoint-load errors + guarded the working-copy read (a crash path); the
  patch-relative "Removed"/comment-only behaviours were confirmed **intentional** and
  documented in `ProjectDiff` remarks + `Gaps.md`.

---

## Queued (not started)

### Before/after node-text detail in the diff canvas
Deferred from the diff viewer's Task 9. The canvas tints nodes; show the per-node
old-vs-new **text** (word-level via the existing `TextDiff`) for a selected node.
Self-contained, builds on shipped code. Needs brainstorm → spec → plan.

### Branch/history navigation
Browse git log, switch branches, attribution. Not started; biggest scope — decompose
during brainstorming.

### Selective-apply polish (smaller, optional)
- Fuller **listed/collapsible dangling-link panel** (v1 ships count-only).
- **First-run intro/tour** for the compare window (deferred; needs persisted "seen" state — see `Gaps.md`).
- **Automatic dependency-pulling** when a selection would dangle (v1 is warn-but-allow).

### Minor diff polish (optional)
- ~~`DiffException.ReadFailed` is used for both IO-read and JSON-parse failures, so a corrupt
  project file shows "locked or unreadable". A `ParseFailed` kind + message would be more precise.~~
  **Done** (2026-06-04): added `DiffExceptionKind.ParseFailed`; `ProjectVersionLoader` throws it on
  deserialization failure, and `DiffViewModel` maps it to the new `Status_DiffParseError` string
  ("the file looks damaged or isn't a valid project file"), distinct from the locked/unreadable message.

See `Gaps.md` for the full known-limitations list.
