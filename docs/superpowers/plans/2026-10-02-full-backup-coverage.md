# Full Backup Coverage Implementation Plan (#123)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore Full Backup restores every file the editor can write: conversations, stringtables in **every** language, and shipped voice-over `.wem` files that Test Patch overwrote. It never restores into the wrong language.

**Architecture:** A new `FullBackup` class in `DialogEditor.Core/Backup` owns the backup's on-disk layout:
- taking the snapshot, which covers conversations plus every installed language and writes a `backup.json` manifest;
- answering which snapshot folder maps onto which live folder at restore time;
- keeping the original of a `.wem` file the first time Test Patch overwrites it (copy-on-write).

Restoring each pair still goes through the existing `FullBackupRestore`, so the #118 guard (only undo editor writes) is unchanged. `MainWindowViewModel` only calls `FullBackup`.

**Tech Stack:** .NET 10, xunit v2, System.Text.Json.

**Spec:** GitHub issue #123, plus the design approved in the session of 2026-10-02 ("approach A"). It's summarised below.

## Design (approved 2026-10-02)

**Backup layout, format 2.** A backup is still `<picked folder>/<timestamp>/`, and the journal stays at `<picked folder>/editor-writes.json`.

```
<timestamp>/
  backup.json                 {"Format": 2, "GameId": "poe2", "Languages": ["de","en",…]}
  conversations/…             (as before)
  stringtables/<lang>/…       one folder per installed language at snapshot time
  voice-over/…                PoE2 only, filled lazily: originals of shipped .wem files F5 overwrote
```

**Restore pairs** (snapshot folder → live folder):
- format 2: conversations; then `stringtables/<lang>` → that language's live folder, for each language in the manifest that's still installed; then `voice-over` → `Voices/English(US)` if it exists;
- format 1 (no `backup.json`, flat `stringtables/`): conversations, plus `stringtables` → the **inferred** language. If no language can be inferred, conversations only, and the status says the backup's text couldn't be matched to a language.

**Inferring a format-1 backup's language.** For each installed language, count the snapshot stringtables whose bytes equal that language's live file. The language with the most matches wins, provided it has at least one and is the only one with that count. Translations differ, so only the original language matches. A tie or zero matches means unknown. This is read-only.

**Voice-over copy-on-write.** In `SyncVoToGame`, before F5 overwrites a `.wem` that already exists in the game, it copies the original to `<latest backup>/voice-over/<relative path>`, unless a copy is already there, so the first original wins. VO writes are also recorded in the editor-write journal, so the #118 guard applies to them. If there's no backup, or the backup folder isn't reachable, nothing is copied and a warning is logged; F6 still works. A `.wem` the editor *adds* is never backed up, and Restore Full Backup doesn't touch it. F6 removes it.

**Older backups are not topped up** with other languages. A top-up could capture files the editor has already changed, which would be the wrong originals.

## Global Constraints

- Strict red/green TDD (CLAUDE.md).
- Every caught exception in production code is logged via `AppLog.Warn`/`AppLog.Error`; no bare `catch {}`.
- Every new user-visible string goes in `DialogEditor.Avalonia/Resources/Strings.axaml`; ViewModels read it via `Loc`. Tests use `StubStringProvider`, so statuses assert on keys.
- Core classes that only handle paths and file formats carry `[NotLocalised("File-format and path handling only")]`, as `FullBackupRestore` does.
- Run tests headless: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`.
- No BOMs. CHANGELOG.md is frozen. Commits reference `#123` and end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## File Structure

| File | Responsibility |
|---|---|
| `DialogEditor.Core/GameData/IGameDataProvider.cs` (modify) | `GetStringTablesRoot(string language)` |
| `DialogEditor.Core/GameData/Poe1GameDataProvider.cs`, `Poe2GameDataProvider.cs` (modify) | implement it |
| `DialogEditor.Core/Backup/FullBackup.cs` (new) | layout: take, manifest, restore pairs, legacy inference, VO copy-on-write |
| `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (modify) | `RunProviderBackupAsync`, `RestoreBackup`, `SyncVoToGame` call `FullBackup` |
| `DialogEditor.Avalonia/Resources/Strings.axaml` (modify) | `Status_FullRestoreLanguageUnknown` |
| `DialogEditor.Tests/Backup/FullBackupTests.cs` (new) | unit tests for `FullBackup` |
| `DialogEditor.Tests/ViewModels/MainWindowViewModelFullRestoreTests.cs` (modify) | end-to-end F5 → lose F6 bookkeeping → Restore Full |
| `README.md` (modify) | *Game updates and your backups* describes the coverage |

---

### Task 1: Pin the wrong-language behaviour and add `GetStringTablesRoot`

**Files:** `DialogEditor.Tests/ViewModels/MainWindowViewModelFullRestoreTests.cs`, `DialogEditor.Tests/GameData/Poe1GameDataProvider…Tests.cs`/`Poe2…`, `IGameDataProvider.cs`, both providers.

- [ ] **Step 1: Characterisation test (expected green).** It confirms #118 already prevents the overwrite, and records today's misleading report. Add to `MainWindowViewModelFullRestoreTests`, with `_game = new FakePoe2Game("en", "de")` in a second test class or a local game. A format-1 snapshot is taken with `en` selected (the existing `TakeSnapshot`). Then switch `provider.Language = "de"` and restore. Assert that the German stringtable is byte-identical afterwards (not overwritten). Name it `LegacyBackup_RestoredWithAnotherLanguageSelected_NeverOverwritesThatLanguage`. Run it → PASS. If it fails, stop: #118's guard is weaker than assumed and the design changes.
- [ ] **Step 2: Red.** Provider tests: `new Poe2GameDataProvider(root).GetStringTablesRoot("de")` ends with `exported/localized/de/text/conversations`. `Poe1GameDataProvider` ends with `data/localized/de/text/conversations`. Neither depends on the `Language` property.
- [ ] **Step 3: Green.** Add to `IGameDataProvider`:

```csharp
    /// The folder holding every conversation stringtable for <paramref name="language"/> — what
    /// the full backup snapshots per language (issue 123). Test doubles that never back up a
    /// game folder needn't implement it.
    string GetStringTablesRoot(string language) => throw new NotSupportedException();
```

Implement it in both providers from their existing `LocalizedRoot`: `Path.Combine(LocalizedRoot, language, "text", "conversations")`. Make each provider's `StringTablesRoot` use it.
- [ ] **Step 4:** Run `--filter "FullyQualifiedName~GameDataProvider|FullyQualifiedName~FullRestore"` → PASS. Commit: `test: pin that a legacy backup never overwrites another language; add GetStringTablesRoot (#123)`.

### Task 2: `FullBackup.TakeAsync` writes format 2

**Files:** `DialogEditor.Core/Backup/FullBackup.cs` (new), `DialogEditor.Tests/Backup/FullBackupTests.cs` (new).

**Produces:**

```csharp
public sealed record FullBackupManifest(int Format, string GameId, IReadOnlyList<string> Languages);

[NotLocalised("File-format and path handling only")]
public static class FullBackup
{
    public const string ManifestFileName = "backup.json";
    public static Task TakeAsync(IGameDataProvider provider, string backupRoot, CancellationToken ct);
    public static FullBackupManifest? ReadManifest(string backupRoot);   // null = format 1 / unreadable
    public static string? Latest(string backupPick);                     // newest <timestamp> folder, or null
}
```

- [ ] **Step 1: Red.** Use `FakePoe2Game.Canonical()` (en + de) from #122. Call `TakeAsync(game.Provider, root)` and assert:
  - `conversations/canonical.conversationbundle` exists;
  - `stringtables/en/canonical.stringtable` and `stringtables/de/canonical.stringtable` exist and are byte-equal to the live files;
  - `ReadManifest(root)` is `Format 2`, `GameId "poe2"`, `Languages ["de","en"]`;
  - no `voice-over/` folder exists (it's filled lazily).

  Also test that `ReadManifest` on a folder without `backup.json` returns `null`, and that a corrupt `backup.json` returns `null` and logs a warning. Test that `Latest` returns the lexicographically last subfolder, or `null` when there's none or the folder is missing.
- [ ] **Step 2:** Run → FAIL (type missing).
- [ ] **Step 3: Green.** `TakeAsync`:
  - `BackupService.BackupAsync(provider.GetBackupRoots().ConversationsRoot, root/conversations)`;
  - for each `provider.AvailableLanguages` whose `GetStringTablesRoot(lang)` exists: `BackupAsync(that, root/stringtables/<lang>)`;
  - then write the manifest **last** (System.Text.Json, indented), so a backup interrupted part-way has no manifest and is treated as untrusted format 1.

  `ReadManifest` catches `JsonException` and `IOException`, logs with `AppLog.Warn`, and returns `null`. `Latest` uses `Directory.Exists` → `GetDirectories().OrderByDescending(d => d, StringComparer.Ordinal).FirstOrDefault()`, the same rule `RestoreBackup` uses today.
- [ ] **Step 4:** Run → PASS. Commit: `feat: full backup snapshots every language and records a manifest (#123)`.

### Task 3: Restore pairs and the language of an older backup

**Produces:**

```csharp
public sealed record RestorePair(string Snapshot, string Live);
public sealed record RestorePlan(IReadOnlyList<RestorePair> Pairs, bool TextLanguageUnknown);
public static RestorePlan PlanRestore(IGameDataProvider provider, string backupRoot, string? voicesRoot);
public static string? InferLegacyLanguage(IGameDataProvider provider, string legacyStringTablesRoot);
```

- [ ] **Step 1: Red.** Test with `FakePoe2Game.Canonical()` and a temp backup root:
  1. **Format 2:** after `TakeAsync`, `PlanRestore(provider, root, voicesRoot: game.VoDir)` → pairs for conversations, `stringtables/de` → `GetStringTablesRoot("de")` and `stringtables/en` → `GetStringTablesRoot("en")`. There's no voice-over pair while `root/voice-over` doesn't exist, and one `voice-over` → `game.VoDir` once it does. `TextLanguageUnknown == false`.
  2. **Format 2, language uninstalled since:** delete the live `de` folder, and the `de` pair is omitted.
  3. **Format 1, inferable:** build the legacy layout by hand (`conversations/` and a flat `stringtables/` copied from the live `de` folder, no manifest). `InferLegacyLanguage` → `"de"`. `PlanRestore` pairs `stringtables` → the `de` root, whatever `provider.Language` is set to.
  4. **Format 1, not inferable:** the flat `stringtables/` holds a file whose bytes match no language. `InferLegacyLanguage` → `null`, and `PlanRestore` has the conversations pair only, with `TextLanguageUnknown == true`.
  5. **Tie:** two languages with identical files (write the same bytes to both live folders) → `null`.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: Green.** Implement as in the Design section. `InferLegacyLanguage` hashes with `FileHash.Of`. For each installed language it counts relative paths where the snapshot hash equals the live hash, and it returns the unique maximum if that's above 0, otherwise `null`. Format 1 is detected as `ReadManifest(root) is null`.
- [ ] **Step 4:** Run → PASS. Commit: `feat: plan a full restore per language, inferring an older backup's language (#123)`.

### Task 4: Wire the view model to `FullBackup`

**Files:** `MainWindowViewModel.cs` (`RunProviderBackupAsync`, `RestoreBackup`), `Strings.axaml`, `MainWindowViewModelFullRestoreTests.cs`.

- [ ] **Step 1: Red.** Change the test's `TakeSnapshot()` to call `FullBackup.TakeAsync(_game.Provider, root, default)`, and give `_game` the languages `"en", "de"`. Give the `EditExisting()` patch translations for both `en` and `de`. New tests:
  - `OtherLanguagesWrittenByTestPatch_AreRestored`: snapshot, F5, lose the F6 bookkeeping, restore → **both** `en` and `de` stringtables are byte-equal to their originals.
  - `LanguageSwitchedSinceTheBackup_RestoresEachLanguageIntoItsOwnFolder`: snapshot with `en` selected, F5, lose the bookkeeping, set the provider and VM language to `de`, restore → both languages are back to their originals and the status has no `Status_FullRestoreSkipped`.
  - `LegacyBackupWithUnknownLanguage_RestoresConversationsAndSaysSo`: hand-built format 1 with an unmatched `stringtables/` → the conversation is restored and the status contains `Status_FullRestoreLanguageUnknown`.
- [ ] **Step 2:** Run → FAIL. The VM still pairs `stringtables` with the current language.
- [ ] **Step 3: Green.**
  - `RunProviderBackupAsync` → `FullBackup.TakeAsync(provider, backupRoot, default)`.
  - `RestoreBackup` → `var backupRoot = FullBackup.Latest(backupPick)`, keeping the existing `null` → `Status_NoBackupFound`.
  - The plan is `FullBackup.PlanRestore(_provider!, backupRoot, IsPoe2 ? VoPathResolver.VoicesRoot(gameDir) : null)`, and it runs `FullBackupRestore.Restore` over `plan.Pairs`.
  - If `plan.TextLanguageUnknown`, append `Loc.Get("Status_FullRestoreLanguageUnknown")` and log `AppLog.Warn`.
  - Add the string to `Strings.axaml` next to `Status_FullRestoreSkipped_*`: `This backup's text files couldn't be matched to an installed language, so only conversations were restored (see the log).`
- [ ] **Step 4:** Run the full restore class and `--filter "Category!=Gui"` → PASS. Commit: `fix: Restore Full Backup restores every language into its own folder (#123)`.

### Task 5: Voice-over copy-on-write and journalling

**Files:** `FullBackup.cs`, `MainWindowViewModel.cs` (`SyncVoToGame`, its caller), `FullBackupTests.cs`, `MainWindowViewModelFullRestoreTests.cs`.

**Produces:**

```csharp
/// Keeps the original of a shipped file the editor is about to overwrite, the first time only.
/// Returns false (and logs) when it couldn't, e.g. the backup folder is unreachable.
public static bool PreserveOriginal(string backupRoot, string liveRoot, string liveFile);
```

It copies `liveFile` to `backupRoot/voice-over/<relative to liveRoot>` unless that copy already exists.

- [ ] **Step 1: Red (unit).**
  - The first call copies the file.
  - A second call after the live file changed leaves the first copy untouched.
  - An unreachable backup root returns `false` and doesn't throw.
- [ ] **Step 2: Red (end-to-end).** In `MainWindowViewModelFullRestoreTests`:
  - Put a project `_vo/narrator/x_0001.wem` with new bytes over an existing game `VoDir/narrator/x_0001.wem` (`existing.wem` bytes). Then: snapshot, F5, lose the bookkeeping, Restore Full → the game `.wem` is back to the original bytes and the status reports it restored.
  - `TestPatchTwice_KeepsTheFirstOriginal`: F5, F6, change `_vo` again, F5 → the backup copy still holds the shipped bytes.
  - `AddedVoFile_IsNotTouchedByFullRestore`: a `_vo` file with no game counterpart → after Restore Full it still exists. Removing it is F6's job.
- [ ] **Step 3:** Run → FAIL.
- [ ] **Step 4: Green.** `SyncVoToGame` gains the `hashBefore` dictionary (pass it from `DoTestPatch`):
  - for each destination, `hashBefore[gameDest] = FileHash.Of(gameDest)` **before** copying;
  - if the file exists and `FullBackup.Latest(AppSettings.GetBackupPath(_currentGameDirectory))` is non-null, call `FullBackup.PreserveOriginal(latest, gameVoRoot, gameDest)`;
  - a `false` result has already been logged inside `PreserveOriginal`, so F5 carries on.

  `RecordEditorWrites(hashBefore)` already runs after the sync, so VO writes enter the journal automatically. Replace the hard-coded voices path in `SyncVoToGame` with `VoPathResolver.VoicesRoot(_currentGameDirectory)`.
- [ ] **Step 5:** Run the backup and full-restore classes, then the fast suite → PASS. Commit: `feat: keep shipped voice-over originals the first time Test Patch overwrites them (#123)`.

### Task 6: Docs and verification in the app

- [ ] **Step 1:** In `README.md`, under *Game updates and your backups*, update the Restore Full Backup bullet:
  - it covers conversations and the text of every installed language;
  - it covers the original of every shipped voice-over line that Test Patch replaced;
  - a voice-over file the editor *added* is removed by F6, not by Restore Full Backup;
  - backups taken before this change cover one language. It's identified automatically, and only that language is restored.
- [ ] **Step 2:** In the running app (`running-the-app`), on a temp copy of `Fixtures/Canonical/poe2`:
  - open the folder;
  - take the first-run backup into a temp folder;
  - confirm `backup.json` and `stringtables/de` + `stringtables/en` exist in it.

  Don't run F5 against anything but the temp copy.
- [ ] **Step 3:** Fast suite → PASS. Commit: `docs: describe what Restore Full Backup covers (#123)`.
