# Node Detail Pane Rework Design

**Date:** 2026-07-02
**Status:** Approved
**Scope:** `NodeDetailView.axaml` (+ code-behind), `NodeDetailViewModel`, `Strings.axaml`,
`NodeDetailViewModel*Tests`. No model/service changes; conditions/scripts editor windows,
canvas, and the VO import dialog are untouched.

---

## Problem

The pane is a flat, always-expanded scroll where the two constantly-used areas —
dialogue text and links — are separated by rarely-touched fields. Confirmed pains:
too long/scrolly, wrong grouping (HideSpeaker under Voice, node ID buried at the
bottom), too dense (raw GUID boxes always doubling the speaker/listener pickers),
and a cramped, cryptic links layout (`→ 42 ⚙0 ✕` with an unlabelled second row).

## Solution — hot-first + collapsible groups (Option A)

Mockups reviewed and approved 2026-07-02 (artifact `detail-pane-options`, version
`initial-three-structures`). Hot core per user: dialogue text + links.

### Pane skeleton (top to bottom)

1. **Node header line** — bold `#<id> · <speaker category> · <node type> · <speaker name>`
   (speaker name only when resolved via speaker data). New testable VM property
   `NodeHeaderSummary`. Replaces the read-only `PropertyGroups` block entirely
   (it contains only the node ID — `RefreshReadOnlyGroups`, `PropertyGroups`, and their
   XAML `ItemsControl` are removed).
2. **Git attribution** — unchanged binding, restyled as a small muted line under the header.
3. **Default / Male text** and **Female text** — unchanged.
4. **Links** — redesigned cards (below) + existing add-link row.
5. Five collapsed **`Expander`s**: *Speaker & Identity*, *Display*, *Voice-over*,
   *Logic*, *Notes*.

### Expander groups

| Group | Contents | Header summary example |
|-------|----------|------------------------|
| Speaker & Identity | Node type, speaker category, speaker picker (+GUID), listener picker (+GUID) | `NPC · Edér → Player` |
| Display | Display type, bark warnings, persistence, actor direction, **HideSpeaker** (moved from Voice) | `Conversation · persists: None` |
| Voice-over | External VO, HasVO, VO status row (`▶ M` / `▶ F` / 🎤 import) | `✓ found · M+F` / `— none` |
| Logic | Scripts summary + Edit button, Conditions summary + Edit button (merged group) | `2 conditions · 1 script` |
| Notes | Comments, translator note | `1 comment` / `—` |

- Header summaries are new VM properties `IdentitySummary`, `DisplaySummary`,
  `VoiceSummary`, `LogicSummary`, `NotesSummary`, localised via `Loc`, refreshed through
  the existing `NotifyAllProxies()` path so a collapsed pane still answers most glances.
- **Expansion state** is per-group, app-session-wide (static backing fields exposed as
  instance VM properties; all default collapsed on launch; retained across `Load()` /
  node selection). Not persisted to `AppSettings` (YAGNI until asked).

### GUID de-doubling

Speaker/listener raw-GUID `TextBox`es are visible only when (a) no speaker data is
loaded — today's fallback behaviour, unchanged — or (b) a small `{}` toggle button next
to the corresponding picker is pressed. The toggle carries a tooltip and
`AutomationProperties.Name`; its state is per-pane, not persisted.

### Link cards

Each `ConnectionViewModel` renders as a bordered card with an accent left edge:

- **Row 1:** `→ <NodeId>` + italic, ellipsised target snippet bound to
  `Target.Owner.TextPreview` (existing property) + delete `✕` at the right.
  The snippet is display-only — navigation stays a canvas concern.
- **Row 2 (labelled controls, all editable in place):**
  - Condition button labelled via new format string `Link_ConditionCount` (`⚙ {0}`),
    accent foreground when `HasConditions`, muted otherwise; same `LinkConditions_Click`
    handler and tooltip as today.
  - Small caption label + QTD `ComboBox` (unchanged binding/options).
  - Small caption label + Weight `NumericUpDown` (unchanged binding).
- Add-link row unchanged apart from moving up with the group.

### New strings (representative keys)

`NodeDetail_HeaderSeparator` (` · `), group titles reused where they exist
(`Label_GroupIdentity`, `Label_GroupDisplay`, `Label_GroupVoice`, `Label_GroupLogic`,
new `Label_GroupNotes`), summary fragments (`NodeDetail_VoFound`, `NodeDetail_VoNone`,
`NodeDetail_VoFoundWithFem`, `NodeDetail_ConditionCount`, `NodeDetail_ScriptCount`,
`NodeDetail_CommentCount`, `NodeDetail_NoneShort`), `Link_ConditionCount`,
`Link_DisplayLabel`, `Link_WeightLabel`, `ToolTip_GuidToggle`,
`AutomationName_GuidToggle_Speaker`, `AutomationName_GuidToggle_Listener`.
Exact key list finalised in the implementation plan; all user-visible text via
resources as always. VM tests assert against keys (the suite's `StubStringProvider`
echoes keys).

## Testing

Strict TDD for all new VM surface, in the existing `NodeDetailViewModel*Tests` style:

- `NodeHeaderSummary` — with and without a resolved speaker name.
- Each `*Summary` across representative states (VO found / missing / with fem;
  0 and n conditions/scripts; empty and non-empty notes).
- Expander state — set expanded, `Load()` another node, still expanded; fresh
  static state defaults collapsed.

XAML restructure verified by build + manual checklist: expander open/collapse with
remembered state, GUID toggle both fallback modes, link card interactions (conditions
window, QTD, weight, delete, add), keyboard tab order sensible, and every control keeps
`ToolTip.Tip` + `AutomationProperties` (the pane is heavily screen-reader annotated and
must stay that way).

## Delivery order (one plan, three tasks — each lands green and committable)

1. **Shared fixes + reorder** — header line (replaces `PropertyGroups`), attribution
   restyle, HideSpeaker → Display, GUID toggle, hot-first reordering of the flat pane.
   This is Option C standing alone.
2. **Expanders + summary properties.**
3. **Link cards.**

User can stop after task 1 or 2 if the added chrome feels wrong in the real app.

## Out of Scope

- Tabs (Option B — rejected: hides glanceable state).
- Persisting expander/GUID-toggle state to `AppSettings`.
- Link navigation from the snippet; canvas changes; conditions/scripts editor windows.
- Batch VO / import dialog surfaces.
