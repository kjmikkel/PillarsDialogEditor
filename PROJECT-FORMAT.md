# Project file formats and versioning

Pillars Dialog Editor's own files are UTF-8 JSON with PascalCase property names, and each
one carries a `SchemaVersion`. From 1.0 these formats are **stable**. Projects are shared
between authors and merged in git, so a version number changes only under the policy below.

(The `.dialogpack` mod archive is described in [FORMAT.md](FORMAT.md), which is also
shipped inside every pack.)

| File | Format | Current `SchemaVersion` |
|---|---|---|
| `.dialogproject` | `DialogProject` | 1 |
| inside a project's `Patches` | `ConversationPatch`, one per conversation | 2 |
| `.patchlist` | `PatchList` (Patch Manager load order) | 1 |

A patch's `SchemaVersion` is independent of its project's. A version-1 project can hold
version-2 patches, and each is checked on its own.

## `.dialogproject`

| Field | Required | Meaning |
|---|---|---|
| `Name` | yes | Project name. |
| `SchemaVersion` | yes | Project format version. |
| `Patches` | yes | Object keyed by conversation name; each value is a `ConversationPatch`. |
| `Layouts` | no | Canvas node positions per conversation (editor metadata). |
| `NewConversations` | no | Conversations the project creates rather than modifies. |
| `Annotations` | no | Canvas annotations per conversation (editor metadata, never written to game files). |
| `IgnoredDuplicates` | no | Duplicate-line allowlist (editor metadata). |

## `ConversationPatch`

A diff against the game's conversation, keyed by node ID.

| Field | Required | Meaning |
|---|---|---|
| `ConversationName` | yes | The conversation this patch applies to. |
| `SchemaVersion` | yes | Patch format version. |
| `AddedNodes` | yes | Complete definitions of new nodes. |
| `DeletedNodeIds` | yes | IDs of removed nodes. |
| `ModifiedNodes` | yes | Per node: `FieldChanges` (each a JSON-encoded `From` / `To` pair, so a patch can check the game still holds the expected value), `AddedLinks`, `DeletedLinks`, `ModifiedLinks`, and optional replace-all `UpdatedConditions` / `UpdatedScripts`. |
| `Translations` | no (added in v2) | Node text per language code, e.g. `"fr"`. |
| `NodeComments` | no (added in v2) | Language-neutral translator notes, keyed by node ID. |

## `.patchlist`

| Field | Required | Meaning |
|---|---|---|
| `SchemaVersion` | yes | Load-order format version. |
| `GameFolder` | yes | The game installation the order was saved for. |
| `Entries` | yes | Mods in load order. Each has a `RelativePath` (resolved against the `.patchlist`'s own folder first) and an `AbsolutePath` fallback. |

## Versioning policy

- **Bump** a format's `SchemaVersion` when an older reader would misread or silently drop
  data: a renamed, removed or re-typed field, a change in meaning, or a new *required* field.
- **Don't bump** for a new *optional* field that older readers can safely ignore.
- **Every bump ships a migration step.** It's one class registered in
  `DialogEditor.Patch/Schema/SchemaMigrator.cs` that rewrites the raw JSON from the old version
  to the new one. Each bump also ships **a fixture file at the old version** under
  `DialogEditor.Tests/Patch/Schema/Fixtures`. A test fails if any version is missing its step.
- **Steps chain.** A file several versions behind is brought forward one step at a time, so
  no step ever has to know about more than one change.
- **Every bump adds a row** to [Patcher compatibility](#patcher-compatibility). A test fails
  if the latest row doesn't match what the code reads.

## What happens on load

- **Current version:** loaded as is.
- **Older version:** migrated in memory. The project is *not* marked modified; the new
  version is written the next time it's saved.
- **Newer version:** refused before anything is read into the editor or written to the game.
  - The editor, and the Patch Manager inside it, explain that a newer version of Pillars
    Dialog Editor is needed.
  - The standalone Patch Manager says the mod needs a newer Pillars Dialog Patcher and links
    to the releases page.
  - A load order is refused as a whole if any of its mods is newer, so the rest is never
    applied without it.
  - `dialog-patcher` exits with code **4** and prints the releases link. Installer scripts
    can treat exit code 4 as "update the patcher".
- **Missing, non-numeric or below-1 version:** treated as a damaged file. Every save writes
  the field.

## Patcher compatibility

A patcher installs a mod only if it reads every format the mod uses, at the mod's version
or newer. Each row gives the newest version of each format that the patcher reads. A mod
made with a newer editor needs a patcher whose row covers its versions.

`dialog-patcher --version` and the standalone Patch Manager's **About** window show the
same versions for the build you're running.

| Patcher | Project | Conversation patch | Load order |
|---|---|---|---|
| 1.0 | ≤ 1 | ≤ 2 | ≤ 1 |

"Patcher" is the Pillars Dialog Patcher version (`PATCHER_VERSION`, released under
`patcher-v*` tags), not the editor's: the two are released independently (GitHub issue 77).
