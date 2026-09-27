# Schema version validation and migration pipeline — design

Issues: #62 (validate schema versions on load, freeze the format for 1.0) and #104 (schema
migration pipeline), both milestone *1.0*, delivered together on one branch. Related: #79 (patcher
compatibility contract, builds on this).

## Problem

`DialogProject.SchemaVersion` (1), `ConversationPatch.SchemaVersion` (2) and
`PatchList.SchemaVersion` (1) are written on save but never checked on load:

- A file from a **newer** editor is read silently. Fields this build doesn't know about are dropped on
  the next save, and the patcher may half-apply a mod whose meaning it doesn't understand.
- A file from an **older** editor has no defined way forward. The first rename or restructure after
  1.0 would silently lose data from every older file.

## Decisions (agreed during brainstorming)

| Question | Decision |
|---|---|
| Newer file in the editor | **Refuse to open.** A localised message names the file's format version and the highest this build reads. Nothing is loaded, so nothing can be saved over it. No read-only mode. |
| Newer file in the CLI | New **exit code 4**: "unsupported file format, update the patcher". Refused in the load phase, before `PatchInstaller`, so nothing is written. |
| Where the check lives | **Inside the three serializers** (`DialogProjectSerializer`, `PatchSerializer`, `PatchListSerializer`). Every load path already goes through them. |
| How older files are handled | A dedicated **`SchemaMigrator`** chains **JSON-level** steps `vN → vN+1` before typed deserialization. It is integrated now, even though the only real step today (`ConversationPatch` 1 → 2) is a no-op. |
| Version bumps in this work | **None.** Project 1, Patch 2, PatchList 1 are frozen as the 1.0 format. |

## Why migrate JSON, not records

An old file's shape may no longer bind to today's records. If v3 renames `Comments` to `Notes`,
deserializing a v1 file into the v3 record drops `Comments` silently, before any post-deserialize step
could move it. So each step rewrites the raw `JsonNode` tree, and typed deserialization only ever sees
current-version JSON. The same up-front parse also reads the version numbers, so a newer file whose
shape changed is refused with the friendly message rather than a JSON type error.

## Components (`DialogEditor.Patch/Schema/`)

### `SchemaFileKind`

`enum { Project, ConversationPatch, PatchList }`, used in exceptions, messages and the step registry.

### `UnsupportedSchemaVersionException`

Carries `Kind`, `Found`, `Supported` and an optional `ConversationName`, which is set when the offender
is a patch nested inside a project. It derives from `InvalidDataException` so existing generic
`catch (Exception)` handlers still treat it as a load failure. Callers that care catch it first.

### `ISchemaMigrationStep` / `SchemaMigrationStep`

`FromVersion` (the step produces `FromVersion + 1`), `Kind`, and `void Apply(JsonObject root)`. Steps
are pure: no I/O and deterministic. The migrator, not the step, sets `SchemaVersion` to the target
afterwards, so a step can't forget.

### `SchemaMigrator`

- `static IReadOnlyList<ISchemaMigrationStep> Steps`: the registry. Today it holds one step,
  `ConversationPatch` 1 → 2 (no-op: `Translations` / `NodeComments` default to empty).
- `JsonObject Migrate(JsonObject root, SchemaFileKind kind, int current)` does the following:
  1. Reads `SchemaVersion`. If it is missing, not an integer, or `< 1`, it throws `InvalidDataException`
     ("not a valid … file"). This can only be a hand-edited or corrupt file, because save always writes
     the field.
  2. `> current` → throws `UnsupportedSchemaVersionException`.
  3. Otherwise it applies the steps for `kind` from the found version up to `current`, in order,
     setting `SchemaVersion` after each one.
  4. For `Project`, it then runs the same procedure on every nested patch under `Patches`, filling in
     `ConversationName` on any exception. This happens after the project's own steps, so a project
     step may restructure `Patches` first.
- An internal overload takes an explicit step list, so tests can check chaining and gap detection
  with test-only steps without touching the real registry.
- `ValidateRegistry(steps, kind, current)`: for each kind, versions `1 .. current-1` each have exactly
  one step (so Project and PatchList, both at 1, have none today). It is checked by a unit test, not at
  runtime.

### Serializers

`Deserialize(string json)` in all three serializers becomes:
`JsonNode.Parse` → `SchemaMigrator.Migrate` → `JsonSerializer.Deserialize<T>(node)`.
`PatchListSerializer` gains a `Deserialize(string)` so its `LoadFromFile` shares the path. `Serialize`
is unchanged. A current-version file produces the same object as today, so a round-trip is
byte-identical.

Migration never marks anything modified. The in-memory object simply has the current version, and the
next real save writes it.

## Surfaces

Each surface catches `UnsupportedSchemaVersionException` specifically, logs it with `AppLog.Warn`, and
then shows the localised message built from `Kind` / `Found` / `Supported` (plus the conversation name
when present).

Example: *"'X' was saved by a newer version of Pillars Dialog Editor (project format 2). This version
reads up to project format 1. Update the editor to open it."*

| Surface | Behaviour |
|---|---|
| Editor: open project | Status message plus the error dialog (existing `ReportError`). The current project is untouched. |
| Editor: autosave recovery | A newer sidecar is **kept**, not deleted as corrupt, because it is someone's work. The editor falls back to loading the saved file, which may be refused in its own right. |
| Editor: git-conflicted project | If either side is newer, the same message replaces "unparseable sides". |
| Editor: merge another project in | Refused with the message. The open project is unchanged. |
| Diff against a git ref | New `DiffExceptionKind.UnsupportedSchema`. The message names the ref. |
| Patch Manager: add `.dialogproject` / `.dialogpack` | The mod is not added, its temp extraction folder is deleted, and the message names the mod. |
| Patch Manager: load `.patchlist` | If the list itself **or any entry** is newer, the whole list is refused, naming the offending mod. The current load order is kept. |
| `dialog-patcher` | Prints the English message (the CLI is `[NotLocalised]` by design), cleans up temp dirs, **exit 4**. Help text documents exit 4. |

Localised strings go in `SharedStrings.axaml`: one message template plus a format-name string per
`SchemaFileKind`.

## Documentation (`FORMAT.md`)

A new section before `.dialogpack`, covering:

- The 1.0 stable format: `.dialogproject` fields (required / optional, nested patches with their own
  version), `ConversationPatch`, `.patchlist`.
- Current versions: Project 1, ConversationPatch 2, PatchList 1.
- **Versioning policy.** Bump when an older reader would misread or drop data (renames, removals,
  meaning changes, required fields). Don't bump for a new optional field that older readers safely
  ignore.
- **Migration rule.** Every bump ships a JSON-level `SchemaMigrator` step and a fixture file at the old
  version.
- Newer files are refused (editor message / CLI exit 4).

## Testing (red first, `DialogEditor.Tests/Patch/Schema/` etc.)

- **Migrator.** The registry has no gaps and no duplicates. Multi-step chaining uses test-only steps.
  The real patch 1 → 2 step is tested. Nested patches are migrated inside a project. A current version
  passes through unchanged. Missing or `< 1` is rejected. A newer root or nested patch throws with the
  right kind, versions and conversation name.
- **Serializers.** A newer file is refused even when its shape would not otherwise parse. A
  current-version file round-trips byte-identical.
- **Fixtures.** A checked-in v1 project containing a v1 patch opens correctly.
- **Editor VM.** A newer project is refused and the previous project is untouched. A newer sidecar is
  kept. An older project is not marked modified after loading.
- **Patch Manager VM.** A newer pack is refused and its temp folder cleaned. A `.patchlist` with a newer
  entry is refused whole, naming the mod.
- **CLI (`PatcherCommandTests`).** A newer project or pack gives exit 4, and the `FakePoe2Game` folder is
  byte-identical afterwards.
- **Localisation.** The new keys resolve.

## Out of scope

- A read-only mode for newer files.
- The patcher-version ↔ format table and the About/`--version` display (#79).
- Any actual schema bump.
