# Dialog Editor — Known Gaps

## Structural (Code Quality)

### ViewModel Test Coverage
35 ViewModel files exist but only 2 have corresponding tests (`NodeDetailViewModelTests`, `ConversationViewModelEditTests`). Complex editing operations — multi-node selection, bulk edits, undo on branching scenarios — are untested and fragile to regression. CLAUDE.md mandates strict TDD; this area is the largest violation of that policy.

**Untested UI-adjacent layers:**
- Views / Converters (26 .cs files) — no unit tests, relies on manual verification
- IGameDataProvider implementations (Poe1/Poe2 data loading not exercised)
- AutoLayoutService algorithm details

---

## Feature Gaps

### Reputation / Faction Conditions
Pillars of Eternity scripted interactions heavily use reputation checks (Positive/Negative with factions like Dozens, Crucible Knights, etc.). If the condition catalogue does not model these well, many real mod scenarios are difficult or impossible to author.

### Batch Operations Across Conversations
Find/Replace works within a single conversation. Mods that touch multiple NPCs — e.g., renaming a location referenced by many characters — require opening each conversation manually. No cross-conversation search or batch edit exists.

### Localization Workflows
No support for exporting strings for translators, importing translated strings back, or managing multiple language variants of a patch.

### Version Control Integration
No built-in diff viewing or merge conflict resolution UI for Git. Collaborating on the same conversation across branches is a manual process.

### Import from Other Formats
Only game XML is supported as a source. No import path from dialogue authoring tools (Articy Draft, etc.) or other intermediate formats.

### Barks System
Display Type toggle exists but no bark-specific tooling — no preview of overhead floating text in context, no bark-specific validation.

### Voice-Over Integration
An "External VO" field exists but there is no path validation, lip-sync metadata support, or audio preview. Mods that add or replace voiced lines have no tooling support. Note: actual voice-over audio is stored in a proprietary archive format — requires investigation before tooling can be designed.
