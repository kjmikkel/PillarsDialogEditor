# Patcher backup, clean-base apply and "Remove all mods" — design

Issue: #76 (milestone *Patcher 1.0*). Related: #66 (editor-side data-safety audit), #6 (cross-mod conflict warnings).

## Problem

The standalone Patch Manager and `dialog-patcher` CLI write straight into the game's current files:

- There is no way back for a player who never installed the editor (the editor's first-run backup and
  **Test ▸ Restore Backup** live only in the editor).
- Every apply patches *already-modified* files. Re-applying the same mod fails with
  `PatchConflictException` (the file already holds each field's `To` value); the GUI has no force option,
  so it cannot re-apply at all, and the CLI with `--force` stacks patches. Removing a mod from the load
  order never reverts it, and #6's conflict detection compares against modded rather than original text.
- The CLI and the GUI each carry their own apply loop and have drifted: the GUI never calls
  `TranslationApplier`, so translations are silently dropped when applying from the Patch Manager.

## Decisions (agreed during brainstorming)

| Question | Decision |
|---|---|
| Where does the backup live? | Inside the game folder: `<game-dir>/PillarsDialogPatcher/`. No settings needed; every tool can find it from the game directory alone, and it travels with the install. |
| What is backed up? | Only files the patcher is about to touch, lazily, the first time each is touched. Each manifest entry is **Overwritten** (original copied) or **Created** (did not exist; restore deletes it). Covers conversations, string tables in every language, and VO `.wem` files. |
| A managed file was changed by someone else? | Detected by hash. **Restore** skips it and reports it. **Apply** stops and asks: GUI confirmation, CLI exit code 3 plus `--accept-current-files`. Never silently adopted. |
| Editor coordination | Detection only. The editor warns when entering Test Patch if the patcher manages any conversation under test. Merging the two backup systems is deferred to #66. |

## Components

All new shared code lives in `DialogEditor.Patch/Install/`.

### `PatcherBackupStore`

Owns `<game-dir>/PillarsDialogPatcher/`:

```
PillarsDialogPatcher/
  manifest.json
  files/<path relative to game-dir>     ← original bytes of every Overwritten entry
```

Manifest entry (paths are relative to the game directory, using `/` separators):

```json
{ "path": "...", "kind": "Overwritten" | "Created",
  "originalSha256": "…" | null, "lastWrittenSha256": "…" | null }
```

The manifest also records a `schemaVersion` (1) and the patcher version that last wrote it.

Operations:

- `EnsureBackedUp(absPath)`: idempotent. On the first sighting of a path it copies the file into `files/`
  and records **Overwritten** with the original hash, or records **Created** if the file does not exist.
  Later calls do nothing, so already-modded bytes are never backed up over the originals.
- `RecordWritten(absPath)`: stores the hash of the bytes now on disk as `lastWrittenSha256`.
- `FindExternallyChanged()`: returns the entries whose current state matches neither the original nor the
  last-written state. A missing file counts as a state: an Overwritten file that was deleted, or a Created
  file that is missing although we wrote it, counts as changed, except where the missing state *is* the
  original (Created, never written).
- `RestoreAll()`: copies back Overwritten entries, deletes Created entries, skips externally changed
  entries, and returns the list of skipped paths. After a successful restore of an entry its
  `lastWrittenSha256` is cleared (the file is back to the original state).
- `AcceptCurrentAsOriginal(paths)`: re-backs up the current bytes of those entries as the new original
  (a file that is absent becomes **Created**) and clears `lastWrittenSha256`.
- The manifest is saved atomically (write a temp file, then `File.Move(..., overwrite: true)`), and it is
  saved **before** each game-file write. Invariant: every file the patcher might have changed is listed in
  the manifest at every instant.
- If the manifest is corrupt or unreadable, loading throws a dedicated `PatcherBackupCorruptException`.
  The store never recreates or overwrites it, because doing so would lose the originals.

### `PatchInstaller`

The single apply/restore routine for the CLI and the Patch Manager.

`Install(gameDir, loadOrder, InstallOptions) → InstallResult`

1. Detect the game (`GameDataProviderFactory`), open the store.
2. `FindExternallyChanged()`. If any are found and `options.AcceptCurrentFiles` is false, return
   `InstallResult.ExternalChanges(paths)` without writing anything. If the option is true, call
   `AcceptCurrentAsOriginal`.
3. `RestoreAll()`: every apply starts from the originals.
4. Merge the load order (existing `PatchMerger` / `MergeWith` behaviour, last-wins, #6 warnings unchanged).
5. For every conversation file, string table (every installed language via `TranslationApplier`) and VO
   file: `EnsureBackedUp` → write → `RecordWritten`. The serializers also leave a `<file>.bak` sidecar
   next to every conversation and string table they overwrite; each sidecar is a write target too, so
   restore removes it (Created) or puts it back (Overwritten). An `InstallOptions.BeforeWrite(absPath)` hook
   runs before each write, so tests can inject a failure partway through.
6. Return `InstallResult.Applied(counts, skipped conversations, restore-skipped paths)`.

On `PatchConflictException` the installer runs `RestoreAll()` again before rethrowing, so the game is left
clean rather than half-modded.

`Restore(gameDir) → RestoreResult` ("Remove all mods") returns the restored and skipped paths.

`DryRun(...)` reports "would restore N files first" in addition to today's plan.

The VO root (currently hard-coded to PoE2 `English(US)` in both the CLI and the VM) moves here, in one
place.

### `PatcherBackupInfo`

A read-only query for the editor: `TryOpen(gameDir)` → does a manifest exist, and `Covers(absPaths)` → which
of these paths it manages. It never throws for a missing manifest; a corrupt manifest reports "unknown"
and is logged.

## User-facing surfaces

All text is localised (`Strings.axaml` / `Loc` resources). Every new control carries a tooltip, and
UIA names come from the labels.

### Patch Manager (GUI)

- **Apply** = reset to the originals, then apply the list. On `ExternalChanges`, a confirmation dialog lists
  up to ~10 file names ("…and N more") with **Treat as new originals** / **Cancel**. Accepting re-runs
  with `AcceptCurrentFiles = true`.
- A new **Remove all mods** button beside Apply. Tooltip: it restores every file the patcher changed and
  deletes files it added. Enabled only when a manifest exists and no apply is running. It asks for
  confirmation first. Skipped (externally changed) files are summarised in the status line and named in
  the log.
- When a game folder is chosen, a status hint says either "N files are backed up by the patcher" or
  "No mods installed with this tool yet".
- The dialogs go through an injectable service interface, so the VM stays testable.

### CLI (`dialog-patcher`)

- `dialog-patcher <game-dir> --restore`: restores and exits.
- `--accept-current-files`: treat externally changed files as the new originals, then apply.
- Exit codes: 0 ok · 1 patch conflict (unchanged) · 2 argument/IO/game-detection error, including a corrupt
  manifest (unchanged meaning) · **3 files changed outside the patcher (new)**. When exiting with code 3,
  the CLI lists the files and names the flag.
- `--help` is updated to match.
- The argument handling moves from top-level statements into a `PatcherCommand.Run(args, stdout, stderr)`
  class inside `DialogEditor.PatchCli`, so it can be tested. `Program.cs` becomes a one-liner.

### Editor

- `TestPatch`: before any write, if `PatcherBackupInfo` shows the patcher manages any conversation or
  string-table path under test, it asks (via the existing dialog seam) whether to continue anyway or to
  cancel and restore with the Patch Manager first. Nothing else in the editor changes.

## Error handling

- An IO failure mid-install leaves the manifest consistent (see the invariant above). The message
  suggests **Remove all mods** / `--restore`. The failure is logged via `AppLog.Error`.
- Patch conflict: restore, then report. The game is left clean.
- Corrupt manifest: apply and restore both refuse. The message names the backup folder and suggests
  verifying the game files; it is logged via `AppLog.Error`.
- Every catch logs, per CLAUDE.md. `OperationCanceledException` is the only exception swallowed.

## Testing (red/green; `DialogEditor.Tests/Patch/Install/`)

The tests use temp game folders with fake PoE1 and PoE2 layouts.

1. **Red first: stacking.** Apply X twice through the current path; show the conflict/stacking. It turns
   green via `PatchInstaller`.
2. Order change: apply [A,B], then [B,A]; the result is byte-identical to [B,A] applied to a pristine copy.
3. Removing a mod: apply [A,B], then [A]; the result equals A alone.
4. Restore is byte-identical for conversations, string tables in ≥2 languages and overwritten VO. Created
   files (a new conversation plus its string tables, a new `.wem`) are deleted.
5. External change: the next install stops and lists the file; restore skips it; accept-current adopts it.
6. Crash safety: an injected writer failure leaves a manifest covering every touched file, and restore
   gets back to the originals.
7. Corrupt manifest: apply and restore both refuse, and the file is untouched.
8. Store units: `EnsureBackedUp` is idempotent, paths are relative with `/`, the manifest round-trips, and
   the save is atomic (the temp file does not remain).
9. CLI via `PatcherCommand.Run`: `--restore`, `--accept-current-files`, exit code 3, and the help text.
10. VMs: Remove-all enabled state and its confirmation, the external-change prompt (accept/cancel), and
    the editor's Test Patch warning fires only when a conversation under test is covered.

Finally, an end-to-end GUI check with the `running-the-app` skill: apply → Remove all mods → the files
are restored.

## Out of scope

- Unifying the editor's backup with the patcher's store (#66).
- A patcher schema-compatibility contract (#79); the manifest carries `schemaVersion` so #79 can use it.
- Multiple VO languages beyond today's hard-coded root (only centralised here, not extended).
