# Dialog Editor — Bug Tracker (pre-launch, internal)

> **Temporary file — delete before the initial public release.** This is a lightweight,
> local bug list for solo development. When the project goes public, bug tracking moves to
> GitHub Issues and this file is removed (see the **Bug Tracker** rule in `CLAUDE.md`).

Newest first. When a bug is fixed, **move** its entry to the **Fixed** section with the
fixing commit hash rather than deleting it — the record of what broke and how it was fixed
stays useful until launch.

## How to log a bug

Copy the template into **Open**. A partial entry is fine — *Repro* + *Actual* is enough to
start. IDs are a simple running counter (`B-001`, `B-002`, …) so commits can reference them
("fix B-003: …").

```
### B-NNN — <one-line summary>
- **Area:** <e.g. Diff viewer, Branches, Changelog reader>
- **Severity:** blocker | major | minor | cosmetic
- **Repro:**
  1. <step>
  2. <step>
- **Expected:** <what should happen>
- **Actual:** <what happens — include any error text or AppLog output>
- **Notes:** <hypotheses, suspect files, related entries>
```

When fixed, append to the moved entry:
```
- **Fixed:** <commit hash> — <one-line explanation of the fix + the test that now guards it>
```

---

## Open

_None yet._

---

## Fixed

### B-001 — Grouped Condition Block has no Edit button and text overflows the window
- **Area:** Condition Editor — branch/group row (`ConditionEditorWindow.axaml`)
- **Severity:** major
- **Repro:**
  1. Open the Condition Editor on a node/link.
  2. Add a group ("+ New group") so a branch (grouped) row renders.
  3. Observe the branch row, especially with a long group summary.
- **Expected:** The branch row shows a short, ellipsis-truncated group summary, with the
  Edit button clustered hard-right alongside the ↑ ↓ ✕ buttons — all always visible. Full
  group text available on hover via tooltip.
- **Actual:** The group text runs off the right edge of the window and the Edit button is
  pushed off-screen with it, so the group cannot be edited.
- **Notes:** Root cause confirmed — the branch row (`NodeDetailView`-style ellipsis is fine
  on leaves) wraps ⚙ icon + name + Edit button in a **horizontal `StackPanel`**
  (`ConditionEditorWindow.axaml` lines ~102–119, `IsVisible="{Binding IsBranch}"`). A
  horizontal StackPanel measures children with infinite width, so the `TextTrimming=
  "CharacterEllipsis"` on the name never fires; the text grows unbounded and shoves the Edit
  button + the ↑ ↓ ✕ buttons (grid columns 3–5) past the window edge. The missing Edit
  button is a symptom of the overflow, not a separate defect.
- **Fix (confirmed layout):** Replace the inner horizontal StackPanel with a width-bounded
  layout (Grid/DockPanel) so the name truncates inside the `*` middle column. Dock the Edit
  button to the right, clustered just **before** the ↑ ↓ ✕ buttons (chosen option: "Cluster
  Edit with ↑ ↓ ✕"). Keep ⚙ icon + truncated name sharing the flexible middle column; show
  the full group text as a tooltip on hover.
- **Fixed:** `da1c3ca` — replaced the inner horizontal StackPanel with a 3-column Grid
  (`Auto,*,Auto`) so the width-bounded `*` column lets the name's `CharacterEllipsis` fire and
  the Edit button docks hard-right next to ↑ ↓ ✕. Full group text shown via a `DisplayName`
  tooltip; explanatory branch tooltip moved to the ⚙ icon. Markup-only fix (no Core logic);
  verified visually by the user — truncation, clickable Edit, and full-text hover all confirmed.
