# Next Steps — pick one at a time

Queued follow-ups from the diff/apply work. Each is self-contained: start a fresh
session, read its entry, and tackle it.

## Completed
- **Diff viewer (Spec 1)** — read-only diff viewer. Shipped on `main`.
- **Selective apply (Spec 2)** — per-node bring-in into the working copy, with applied
  preview, save-guard, single-step undo, dangling warning, and a plain-language Help
  window. Shipped on `main`.
- **Whole-feature review of the read-only diff viewer** (2026-05-31) — localized
  endpoint-load errors + guarded the working-copy read; patch-relative "Removed" and
  comment-only behaviours confirmed **intentional** and documented in `ProjectDiff`.
- **Before/after node-text detail** — per-node old-vs-new text (word-level `TextDiff`)
  for a selected node in the diff canvas. Shipped.
- **Multi-language before/after detail** — stacked per-language sections for every
  language whose text changed. Shipped.
- **Listed/collapsible dangling-link panel** — replaced the v1 count-only warning.
  Shipped.
- **Automatic dependency-pulling** — ticking a change auto-ticks the added nodes it
  links to (transitive, outgoing-only, default-on toggle). Shipped.
- **Diff `ParseFailed` kind** (2026-06-04) — corrupt project files report "looks
  damaged", distinct from locked/unreadable. Shipped.
- **Female-variant conflict text detail** (2026-06-04) — the git conflict dialog shows
  Default and Female text as separate labelled rows when a node has female text, fixing
  both the female-only and both-variants-differ display cases. Shipped. Spec/plan:
  `docs/superpowers/specs/2026-06-04-git-conflict-female-text-detail-design.md`,
  `docs/superpowers/plans/2026-06-04-git-conflict-female-text-detail.md`.
- **History browser** (2026-06-04) — git history timeline for the open project;
  "Compare with my copy" opens a commit in the compare window. Spec/plan:
  `docs/superpowers/specs/2026-06-04-history-browser-design.md`,
  `docs/superpowers/plans/2026-06-04-history-browser.md`.
- **Attribution / blame** (2026-06-04) — per-node "last edited by" from
  `git blame --line-porcelain HEAD`, mapped onto nodes via `DialogProjectLineMap`.
  Standalone Attribution window + a "Last edited" line in the node detail panel.
  HEAD-based, computed at project open (see `Gaps.md` for limitations).
- **Branch management** (2026-06-06) — switch/create/rename/delete local branches in a
  dedicated window; commit-then-switch with informed consent (tracked-only), case-A
  untracked block surfaced, force-delete behind a confirm. Distinct git-not-installed
  message added to History/Diff/Blame too. Spec/plan:
  `docs/superpowers/specs/2026-06-05-branch-management-design.md`,
  `docs/superpowers/plans/2026-06-06-branch-management.md`.

---

## Queued (not started)

### Blocked on investigation (can't design yet)
- **Bark preview** — in-context preview of overhead floating bark text. Needs
  reverse-engineering the game's bark rendering (font, line-wrapping, max width) first.
- **Voice-over integration** — path validation, lip-sync metadata, audio preview. Needs
  the proprietary audio archive format investigated first.

### Deferred (revisit)
- **First-run intro/tour** for the compare/apply window — a one-time dismissible
  orientation panel. Deferred because it needs persisted "seen" state; the always-on
  in-context cues cover the immediate need. See `Gaps.md`.

See `Gaps.md` for the full known-limitations list.
