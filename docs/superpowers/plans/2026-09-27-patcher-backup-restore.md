# Patcher Backup, Clean-Base Apply and "Remove All Mods" Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every patcher apply start from backed-up original game files, and let players undo all mods from the Patch Manager and `dialog-patcher --restore` (issue #76).

**Architecture:** A new `DialogEditor.Patch/Install/` namespace holds `PatcherBackupStore`, a per-file manifest backup in `<game-dir>/PillarsDialogPatcher/`, and `PatchInstaller`, the one restore-then-apply routine that both the CLI and the Patch Manager VM now call. A read-only `PatcherBackupInfo` lets the editor warn before Test Patch runs over patcher-managed files.

**Tech Stack:** .NET 10, C#, xUnit, CommunityToolkit.Mvvm, Avalonia 11, System.Text.Json, SHA-256 (`System.Security.Cryptography`).

**Spec:** `docs/superpowers/specs/2026-09-27-patcher-backup-restore-design.md`

## Global Constraints

- Strict red/green TDD: every behaviour gets a failing test first (CLAUDE.md).
- No user-visible text inline in XAML or in the C# of `Core/Patch/ViewModels/Avalonia*`. Patch Manager text goes in `DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml`; editor text in `DialogEditor.Avalonia/Resources/Strings.axaml`. The CLI is English-only by established convention. Mark CLI and diagnostic types with `[NotLocalised("reason")]` (`using DialogEditor.Core.Localisation;`), as `CrossModConflictReport` does.
- Every interactive control gets `ToolTip.Tip` plus `AutomationProperties.HelpText` from resources. OK/Cancel on a confirmation dialog are exempt.
- Every new `<Window>` has `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`. In `Avalonia.Shared` and the standalone PatchManager, use that app's own asset URI; check how `ThemeOnboardingWindow.axaml` does it and copy that.
- Every production `catch` logs via `AppLog.Error`/`AppLog.Warn` (`DialogEditor.Core.Logging`). Only `OperationCanceledException` is swallowed. No bare `catch { }`.
- Backup folder name: `PillarsDialogPatcher`; manifest `manifest.json`; copies under `files/<relative path>`; manifest `schemaVersion` = 1; relative paths use `/`.
- CLI exit codes: 0 ok · 1 patch conflict · 2 argument/IO/detection/corrupt manifest · **3 files changed outside the patcher**.
- Headless test command (never bare `dotnet test`, it launches the GUI): `dotnet test DialogEditor.Tests --filter "Category!=Gui"`. Tests run serially by design.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and reference `#76`.
- `CHANGELOG.md` is frozen: do not touch it.

## File Structure

| File | Responsibility |
|---|---|
| Create `DialogEditor.Patch/Install/PatcherManifest.cs` | Manifest records (`BackupEntry`, `BackupEntryKind`, `PatcherManifest`) |
| Create `DialogEditor.Patch/Install/PatcherBackupCorruptException.cs` | Thrown when the manifest can't be read |
| Create `DialogEditor.Patch/Install/PatcherBackupStore.cs` | Backup folder + manifest: back up, record, detect external change, restore, accept |
| Create `DialogEditor.Patch/Install/PatchInstaller.cs` | Restore-then-apply, `Restore`, `Plan` (dry run); VO root lives here |
| Create `DialogEditor.Patch/Install/InstallModels.cs` | `InstallEntry`, `InstallOptions`, `InstallResult`, `RestoreResult`, `InstallPlan` |
| Create `DialogEditor.Patch/Install/PatcherBackupInfo.cs` | Read-only "does the patcher manage these paths?" for the editor |
| Modify `DialogEditor.Patch/TranslationApplier.cs` | Add `TargetPaths(...)` so the installer knows what it will write |
| Create `DialogEditor.PatchCli/PatcherCommand.cs` | All CLI logic, testable `Run(args, stdout, stderr)` |
| Modify `DialogEditor.PatchCli/Program.cs` | One-liner calling `PatcherCommand.Run` |
| Modify `DialogEditor.ViewModels/ViewModels/PatchManagerViewModel.cs` | Apply via installer; Remove all mods; external-change prompt; backup status |
| Create `DialogEditor.Avalonia.Shared/ConfirmDialog.axaml(.cs)` | Generic localised yes/no dialog with an optional file list |
| Create `DialogEditor.Avalonia.Shared/PatchManagerDialogs.cs` | Wires the VM's confirm callbacks to `ConfirmDialog` for both hosts |
| Modify `DialogEditor.Avalonia.Shared/PatchManagerView.axaml` | Remove all mods button + backup status line |
| Modify `DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml` | New Patch Manager strings |
| Modify `DialogEditor.PatchManager/MainWindow.axaml.cs`, `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` | Call `PatchManagerDialogs.Attach` |
| Modify `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` | Test Patch warning via `ConfirmTestOverPatcherMods` |
| Modify `DialogEditor.Avalonia/Resources/Strings.axaml` | Editor warning strings |
| Create `DialogEditor.Tests/Helpers/FakePoe2Game.cs` | On-disk fake PoE2 install for installer tests |
| Create `DialogEditor.Tests/Patch/Install/*.cs` | Store, installer, info tests |
| Create `DialogEditor.Tests/PatchCli/PatcherCommandTests.cs` | CLI tests |
| Modify `DialogEditor.Tests/DialogEditor.Tests.csproj` | Reference `DialogEditor.PatchCli` |

---

### Task 1: Backup store — back up, record, persist

**Files:**
- Create: `DialogEditor.Patch/Install/PatcherManifest.cs`
- Create: `DialogEditor.Patch/Install/PatcherBackupCorruptException.cs`
- Create: `DialogEditor.Patch/Install/PatcherBackupStore.cs`
- Test: `DialogEditor.Tests/Patch/Install/PatcherBackupStoreTests.cs`

**Interfaces:**
- Produces:
  - `enum BackupEntryKind { Overwritten, Created }`
  - `sealed record BackupEntry(string Path, BackupEntryKind Kind, string? OriginalSha256, string? LastWrittenSha256)`: `Path` is relative with `/`.
  - `sealed record PatcherManifest(int SchemaVersion, string PatcherVersion, IReadOnlyList<BackupEntry> Entries)` with `const int CurrentSchemaVersion = 1`
  - `class PatcherBackupCorruptException(string manifestPath, string reason, Exception? inner) : Exception`, with property `ManifestPath`
  - `sealed class PatcherBackupStore`:
    - `const string FolderName = "PillarsDialogPatcher"`, `const string ManifestFileName = "manifest.json"`
    - `static PatcherBackupStore Open(string gameDir)`: throws `PatcherBackupCorruptException`
    - `static bool Exists(string gameDir)`
    - `string GameDir`, `string BackupRoot`, `string ManifestPath`
    - `IReadOnlyList<BackupEntry> Entries`
    - `void EnsureBackedUp(string absPath)`, `void RecordWritten(string absPath)`
    - `string ToRelative(string absPath)`, `string ToAbsolute(string relPath)`
    - `static string? HashFile(string absPath)`: null when the file is missing

- [ ] **Step 1: Write the failing tests**

```csharp
// DialogEditor.Tests/Patch/Install/PatcherBackupStoreTests.cs
using System.Text.Json;
using DialogEditor.Patch.Install;

namespace DialogEditor.Tests.Patch.Install;

public class PatcherBackupStoreTests : IDisposable
{
    private readonly string _game = Path.Combine(Path.GetTempPath(), $"pbs_{Guid.NewGuid():N}");

    public PatcherBackupStoreTests() => Directory.CreateDirectory(_game);
    public void Dispose() { try { Directory.Delete(_game, true); } catch (Exception) { /* best-effort */ } }

    private string Write(string rel, string text)
    {
        var abs = Path.Combine(_game, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, text);
        return abs;
    }

    [Fact]
    public void EnsureBackedUp_ExistingFile_CopiesOriginalAndRecordsOverwritten()
    {
        var abs   = Write("data/a.txt", "original");
        var store = PatcherBackupStore.Open(_game);

        store.EnsureBackedUp(abs);

        var e = Assert.Single(store.Entries);
        Assert.Equal("data/a.txt", e.Path);
        Assert.Equal(BackupEntryKind.Overwritten, e.Kind);
        Assert.Equal(PatcherBackupStore.HashFile(abs), e.OriginalSha256);
        Assert.Equal("original", File.ReadAllText(
            Path.Combine(_game, "PillarsDialogPatcher", "files", "data", "a.txt")));
    }

    [Fact]
    public void EnsureBackedUp_MissingFile_RecordsCreated()
    {
        var store = PatcherBackupStore.Open(_game);
        store.EnsureBackedUp(Path.Combine(_game, "data", "new.txt"));

        var e = Assert.Single(store.Entries);
        Assert.Equal(BackupEntryKind.Created, e.Kind);
        Assert.Null(e.OriginalSha256);
    }

    [Fact]
    public void EnsureBackedUp_IsIdempotent_DoesNotBackUpModdedBytes()
    {
        var abs   = Write("data/a.txt", "original");
        var store = PatcherBackupStore.Open(_game);
        store.EnsureBackedUp(abs);
        File.WriteAllText(abs, "modded");

        store.EnsureBackedUp(abs);

        Assert.Single(store.Entries);
        Assert.Equal("original", File.ReadAllText(
            Path.Combine(_game, "PillarsDialogPatcher", "files", "data", "a.txt")));
    }

    [Fact]
    public void RecordWritten_StoresHashOfCurrentBytes()
    {
        var abs   = Write("data/a.txt", "original");
        var store = PatcherBackupStore.Open(_game);
        store.EnsureBackedUp(abs);
        File.WriteAllText(abs, "modded");

        store.RecordWritten(abs);

        Assert.Equal(PatcherBackupStore.HashFile(abs), Assert.Single(store.Entries).LastWrittenSha256);
    }

    [Fact]
    public void Manifest_IsPersistedAndReloads_WithoutTempFileLeftBehind()
    {
        var abs = Write("data/a.txt", "original");
        PatcherBackupStore.Open(_game).EnsureBackedUp(abs);

        var reopened = PatcherBackupStore.Open(_game);

        Assert.Equal("data/a.txt", Assert.Single(reopened.Entries).Path);
        Assert.False(File.Exists(reopened.ManifestPath + ".tmp"));
        using var doc = JsonDocument.Parse(File.ReadAllText(reopened.ManifestPath));
        Assert.Equal(1, doc.RootElement.GetProperty("SchemaVersion").GetInt32());
    }

    [Fact]
    public void Exists_FalseUntilSomethingIsBackedUp()
    {
        Assert.False(PatcherBackupStore.Exists(_game));
        PatcherBackupStore.Open(_game).EnsureBackedUp(Write("a.txt", "x"));
        Assert.True(PatcherBackupStore.Exists(_game));
    }

    [Fact]
    public void Open_CorruptManifest_ThrowsAndLeavesFileUntouched()
    {
        var manifest = Write("PillarsDialogPatcher/manifest.json", "{ not json");

        Assert.Throws<PatcherBackupCorruptException>(() => PatcherBackupStore.Open(_game));
        Assert.Equal("{ not json", File.ReadAllText(manifest));
    }

    [Fact]
    public void Open_NewerSchema_Throws()
    {
        Write("PillarsDialogPatcher/manifest.json",
              """{"SchemaVersion":99,"PatcherVersion":"9.9","Entries":[]}""");
        Assert.Throws<PatcherBackupCorruptException>(() => PatcherBackupStore.Open(_game));
    }

    [Fact]
    public void EnsureBackedUp_PathOutsideGameDir_Throws()
    {
        var store = PatcherBackupStore.Open(_game);
        Assert.Throws<ArgumentException>(() =>
            store.EnsureBackedUp(Path.Combine(Path.GetTempPath(), "elsewhere.txt")));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatcherBackupStoreTests"`
Expected: build FAIL — `DialogEditor.Patch.Install` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
// DialogEditor.Patch/Install/PatcherManifest.cs
namespace DialogEditor.Patch.Install;

/// Overwritten: the file existed before the patcher first wrote it; its original bytes are kept
/// under files/. Created: the file did not exist, so restoring means deleting it.
public enum BackupEntryKind { Overwritten, Created }

/// One file the patcher manages. Path is relative to the game directory with '/' separators
/// so a manifest stays valid if the install folder is moved. LastWrittenSha256 is the hash of
/// what the patcher itself last wrote (null = not written since backup or restore); a file
/// matching neither hash was changed by someone else (a game update, the editor, another tool).
public sealed record BackupEntry(
    string          Path,
    BackupEntryKind Kind,
    string?         OriginalSha256,
    string?         LastWrittenSha256);

public sealed record PatcherManifest(
    int                        SchemaVersion,
    string                     PatcherVersion,
    IReadOnlyList<BackupEntry> Entries)
{
    public const int CurrentSchemaVersion = 1;
}
```

```csharp
// DialogEditor.Patch/Install/PatcherBackupCorruptException.cs
using DialogEditor.Core.Localisation;

namespace DialogEditor.Patch.Install;

/// The manifest could not be read. Never "repaired" by recreating it: that would discard the
/// only record of which originals the backup holds.
[NotLocalised("Diagnostic message; the UI and CLI render ManifestPath with their own copy")]
public sealed class PatcherBackupCorruptException(string manifestPath, string reason, Exception? inner = null)
    : Exception($"Patcher backup manifest '{manifestPath}' is unreadable: {reason}", inner)
{
    public string ManifestPath { get; } = manifestPath;
}
```

```csharp
// DialogEditor.Patch/Install/PatcherBackupStore.cs
using System.Security.Cryptography;
using System.Text.Json;
using DialogEditor.Core.Localisation;

namespace DialogEditor.Patch.Install;

/// <summary>
/// The patcher's own safety net, kept inside the game folder (issue #76) so the CLI, the
/// Patch Manager and the editor all find it from the game directory alone, with no settings.
///
/// Files are backed up lazily, the first time the patcher is about to write each one, so the
/// backup holds only what mods actually touch. Invariant: the manifest is saved *before* each
/// game-file write, so at every instant it lists every file the patcher might have changed —
/// a crash mid-apply can always be undone.
/// </summary>
[NotLocalised("File-format and path handling only")]
public sealed class PatcherBackupStore
{
    public const string FolderName       = "PillarsDialogPatcher";
    public const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly List<BackupEntry> _entries;

    private PatcherBackupStore(string gameDir, List<BackupEntry> entries)
    {
        GameDir  = Path.GetFullPath(gameDir);
        _entries = entries;
    }

    public string GameDir      { get; }
    public string BackupRoot   => Path.Combine(GameDir, FolderName);
    public string ManifestPath => Path.Combine(BackupRoot, ManifestFileName);
    private string FilesRoot   => Path.Combine(BackupRoot, "files");

    public IReadOnlyList<BackupEntry> Entries => _entries;

    public static bool Exists(string gameDir) =>
        File.Exists(Path.Combine(gameDir, FolderName, ManifestFileName));

    public static PatcherBackupStore Open(string gameDir)
    {
        var manifestPath = Path.Combine(Path.GetFullPath(gameDir), FolderName, ManifestFileName);
        if (!File.Exists(manifestPath))
            return new PatcherBackupStore(gameDir, []);

        PatcherManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PatcherManifest>(File.ReadAllText(manifestPath));
        }
        catch (JsonException ex)
        {
            throw new PatcherBackupCorruptException(manifestPath, "invalid JSON", ex);
        }

        if (manifest is null || manifest.Entries is null)
            throw new PatcherBackupCorruptException(manifestPath, "empty manifest");
        if (manifest.SchemaVersion is < 1 or > PatcherManifest.CurrentSchemaVersion)
            throw new PatcherBackupCorruptException(manifestPath,
                $"unsupported schema version {manifest.SchemaVersion}");

        return new PatcherBackupStore(gameDir, [.. manifest.Entries]);
    }

    public void EnsureBackedUp(string absPath)
    {
        var rel = ToRelative(absPath);
        if (IndexOf(rel) >= 0) return;   // idempotent: never back up already-modded bytes

        if (File.Exists(absPath))
        {
            var copy = BackupCopyPath(rel);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(absPath, copy, overwrite: true);   // copy first: the manifest never points at a missing copy
            _entries.Add(new BackupEntry(rel, BackupEntryKind.Overwritten, HashFile(absPath), null));
        }
        else
        {
            _entries.Add(new BackupEntry(rel, BackupEntryKind.Created, null, null));
        }
        Save();
    }

    public void RecordWritten(string absPath)
    {
        var i = IndexOf(ToRelative(absPath));
        if (i < 0) throw new InvalidOperationException($"'{absPath}' was written without being backed up first.");
        _entries[i] = _entries[i] with { LastWrittenSha256 = HashFile(absPath) };
        Save();
    }

    public string ToRelative(string absPath)
    {
        var rel = Path.GetRelativePath(GameDir, Path.GetFullPath(absPath));
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
            throw new ArgumentException($"'{absPath}' is outside the game directory '{GameDir}'.", nameof(absPath));
        return rel.Replace('\\', '/');
    }

    public string ToAbsolute(string relPath) =>
        Path.Combine(GameDir, relPath.Replace('/', Path.DirectorySeparatorChar));

    public static string? HashFile(string absPath) =>
        File.Exists(absPath) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(absPath))) : null;

    private string BackupCopyPath(string rel) =>
        Path.Combine(FilesRoot, rel.Replace('/', Path.DirectorySeparatorChar));

    private int IndexOf(string rel) =>
        _entries.FindIndex(e => string.Equals(e.Path, rel, StringComparison.OrdinalIgnoreCase));

    /// Atomic: write a sibling temp file, then move it over the manifest, so a crash leaves
    /// either the old or the new manifest, never a torn one.
    private void Save()
    {
        Directory.CreateDirectory(BackupRoot);
        var manifest = new PatcherManifest(
            PatcherManifest.CurrentSchemaVersion,
            AppVersion.FromAssembly(typeof(PatcherBackupStore).Assembly),
            _entries);
        var tmp = ManifestPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(tmp, ManifestPath, overwrite: true);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatcherBackupStoreTests"`
Expected: PASS (9 tests). Also run `--filter "FullyQualifiedName~HardcodedString"` to confirm the localisation scanner still passes.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/Install DialogEditor.Tests/Patch/Install
git commit -m "feat(patch): per-file backup store with atomic manifest (#76)"
```

---

### Task 2: Backup store — detect external change, restore, accept

**Files:**
- Modify: `DialogEditor.Patch/Install/PatcherBackupStore.cs`
- Test: `DialogEditor.Tests/Patch/Install/PatcherBackupStoreRestoreTests.cs`

**Interfaces:**
- Consumes: Task 1's `PatcherBackupStore`.
- Produces:
  - `IReadOnlyList<string> FindExternallyChanged()`: relative paths
  - `IReadOnlyList<string> RestoreAll()`: returns the relative paths it skipped
  - `void AcceptCurrentAsOriginal(IEnumerable<string> relPaths)`
  - `void DeleteBackup()`: removes the whole `PillarsDialogPatcher` folder

- [ ] **Step 1: Write the failing tests**

```csharp
// DialogEditor.Tests/Patch/Install/PatcherBackupStoreRestoreTests.cs
using DialogEditor.Patch.Install;

namespace DialogEditor.Tests.Patch.Install;

public class PatcherBackupStoreRestoreTests : IDisposable
{
    private readonly string _game = Path.Combine(Path.GetTempPath(), $"pbsr_{Guid.NewGuid():N}");

    public PatcherBackupStoreRestoreTests() => Directory.CreateDirectory(_game);
    public void Dispose() { try { Directory.Delete(_game, true); } catch (Exception) { /* best-effort */ } }

    private string P(string rel) => Path.Combine(_game, rel);

    private void Write(string rel, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(P(rel))!);
        File.WriteAllText(P(rel), text);
    }

    /// Mimics one patcher write: back up, write, record.
    private static void PatcherWrites(PatcherBackupStore s, string abs, string text)
    {
        s.EnsureBackedUp(abs);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, text);
        s.RecordWritten(abs);
    }

    [Fact]
    public void RestoreAll_PutsBackOverwritten_AndDeletesCreated()
    {
        Write("a.txt", "original");
        var s = PatcherBackupStore.Open(_game);
        PatcherWrites(s, P("a.txt"), "modded");
        PatcherWrites(s, P("new/b.txt"), "added");

        var skipped = s.RestoreAll();

        Assert.Empty(skipped);
        Assert.Equal("original", File.ReadAllText(P("a.txt")));
        Assert.False(File.Exists(P("new/b.txt")));
        Assert.All(s.Entries, e => Assert.Null(e.LastWrittenSha256));
    }

    [Fact]
    public void FindExternallyChanged_EmptyWhenFilesAreOriginalOrOurs()
    {
        Write("a.txt", "original");
        var s = PatcherBackupStore.Open(_game);
        PatcherWrites(s, P("a.txt"), "modded");
        PatcherWrites(s, P("b.txt"), "added");
        Assert.Empty(s.FindExternallyChanged());

        s.RestoreAll();
        Assert.Empty(s.FindExternallyChanged());
    }

    [Fact]
    public void FindExternallyChanged_ReportsFileChangedByOthers()
    {
        Write("a.txt", "original");
        var s = PatcherBackupStore.Open(_game);
        PatcherWrites(s, P("a.txt"), "modded");
        File.WriteAllText(P("a.txt"), "game update");

        Assert.Equal(["a.txt"], s.FindExternallyChanged());
    }

    [Fact]
    public void FindExternallyChanged_ReportsDeletedOverwrittenFile()
    {
        Write("a.txt", "original");
        var s = PatcherBackupStore.Open(_game);
        PatcherWrites(s, P("a.txt"), "modded");
        File.Delete(P("a.txt"));

        Assert.Equal(["a.txt"], s.FindExternallyChanged());
    }

    [Fact]
    public void RestoreAll_SkipsExternallyChangedFile_AndLeavesItAsIs()
    {
        Write("a.txt", "original");
        var s = PatcherBackupStore.Open(_game);
        PatcherWrites(s, P("a.txt"), "modded");
        File.WriteAllText(P("a.txt"), "game update");

        var skipped = s.RestoreAll();

        Assert.Equal(["a.txt"], skipped);
        Assert.Equal("game update", File.ReadAllText(P("a.txt")));
    }

    [Fact]
    public void AcceptCurrentAsOriginal_AdoptsNewBytes()
    {
        Write("a.txt", "original");
        var s = PatcherBackupStore.Open(_game);
        PatcherWrites(s, P("a.txt"), "modded");
        File.WriteAllText(P("a.txt"), "game update");

        s.AcceptCurrentAsOriginal(["a.txt"]);

        Assert.Empty(s.FindExternallyChanged());
        PatcherWrites(s, P("a.txt"), "modded again");
        s.RestoreAll();
        Assert.Equal("game update", File.ReadAllText(P("a.txt")));
    }

    [Fact]
    public void AcceptCurrentAsOriginal_MissingFileBecomesCreated()
    {
        Write("a.txt", "original");
        var s = PatcherBackupStore.Open(_game);
        PatcherWrites(s, P("a.txt"), "modded");
        File.Delete(P("a.txt"));

        s.AcceptCurrentAsOriginal(["a.txt"]);

        Assert.Equal(BackupEntryKind.Created, Assert.Single(s.Entries).Kind);
    }

    [Fact]
    public void DeleteBackup_RemovesFolder()
    {
        var s = PatcherBackupStore.Open(_game);
        PatcherWrites(s, P("a.txt"), "x");
        s.DeleteBackup();
        Assert.False(PatcherBackupStore.Exists(_game));
        Assert.False(Directory.Exists(s.BackupRoot));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatcherBackupStoreRestoreTests"`
Expected: build FAIL — `RestoreAll`, `FindExternallyChanged`, `AcceptCurrentAsOriginal` and `DeleteBackup` are not defined.

- [ ] **Step 3: Implement (add to `PatcherBackupStore`)**

```csharp
    /// A file's "original state" is its backed-up hash (Overwritten) or absence (Created);
    /// null stands for "file absent" throughout.
    private static string? OriginalState(BackupEntry e) =>
        e.Kind == BackupEntryKind.Overwritten ? e.OriginalSha256 : null;

    private bool IsExternallyChanged(BackupEntry e)
    {
        var current = HashFile(ToAbsolute(e.Path));
        if (current == OriginalState(e)) return false;
        if (e.LastWrittenSha256 is not null && current == e.LastWrittenSha256) return false;
        return true;
    }

    public IReadOnlyList<string> FindExternallyChanged() =>
        _entries.Where(IsExternallyChanged).Select(e => e.Path).ToList();

    public IReadOnlyList<string> RestoreAll()
    {
        var skipped = new List<string>();
        for (var i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (IsExternallyChanged(e)) { skipped.Add(e.Path); continue; }

            var abs = ToAbsolute(e.Path);
            if (e.Kind == BackupEntryKind.Overwritten)
            {
                if (HashFile(abs) != e.OriginalSha256)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
                    File.Copy(BackupCopyPath(e.Path), abs, overwrite: true);
                }
            }
            else if (File.Exists(abs))
            {
                File.Delete(abs);
            }
            _entries[i] = e with { LastWrittenSha256 = null };
        }
        Save();
        return skipped;
    }

    public void AcceptCurrentAsOriginal(IEnumerable<string> relPaths)
    {
        foreach (var rel in relPaths)
        {
            var i = IndexOf(rel);
            if (i < 0) continue;
            var abs  = ToAbsolute(_entries[i].Path);
            var copy = BackupCopyPath(_entries[i].Path);
            if (File.Exists(abs))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(abs, copy, overwrite: true);
                _entries[i] = _entries[i] with
                {
                    Kind = BackupEntryKind.Overwritten, OriginalSha256 = HashFile(abs), LastWrittenSha256 = null,
                };
            }
            else
            {
                if (File.Exists(copy)) File.Delete(copy);
                _entries[i] = _entries[i] with
                {
                    Kind = BackupEntryKind.Created, OriginalSha256 = null, LastWrittenSha256 = null,
                };
            }
        }
        Save();
    }

    public void DeleteBackup()
    {
        if (Directory.Exists(BackupRoot))
            Directory.Delete(BackupRoot, recursive: true);
        _entries.Clear();
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatcherBackupStore"`
Expected: PASS (all tests from Tasks 1 and 2).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/Install/PatcherBackupStore.cs DialogEditor.Tests/Patch/Install/PatcherBackupStoreRestoreTests.cs
git commit -m "feat(patch): restore, external-change detection and accept for the backup store (#76)"
```

---

### Task 3: `PatchInstaller` — apply onto a clean base (fixes stacking)

**Files:**
- Create: `DialogEditor.Patch/Install/InstallModels.cs`
- Create: `DialogEditor.Patch/Install/PatchInstaller.cs`
- Modify: `DialogEditor.Patch/TranslationApplier.cs`
- Create: `DialogEditor.Tests/Helpers/FakePoe2Game.cs`
- Test: `DialogEditor.Tests/Patch/Install/PatchInstallerTests.cs`

**Interfaces:**
- Consumes: `PatcherBackupStore` (Tasks 1–2); `DialogProject.MergeWith`, `DialogProject.IsNewConversation`, `PatchApplier.Apply(baseSnap, patch, ignoreConflicts)`, `ConversationSnapshotBuilder.Build(conversation)`, `IGameDataProvider` (`FindConversation`, `BuildNewConversationFile`, `InitializeConversationFile`, `LoadConversation`, `SaveConversation`, `GetStringTablePath(file, lang)`, `AvailableLanguages`, `GameId`).
- Produces:
  - `sealed record InstallEntry(DialogProject Project, string? VoFolder = null)`
  - `sealed record InstallOptions(bool Force = false, bool AcceptCurrentFiles = false) { public Action<string>? BeforeWrite { get; init; } }`
  - `abstract record InstallResult` with nested `sealed record Applied(int ConversationsPatched, IReadOnlyList<string> MissingConversations, IReadOnlyList<string> RestoreSkipped, int VoFilesCopied)` and `sealed record ExternalChanges(IReadOnlyList<string> Paths)`
  - `static class PatchInstaller`: `static InstallResult Install(IGameDataProvider provider, string gameDir, IReadOnlyList<InstallEntry> loadOrder, InstallOptions options)`
  - `TranslationApplier.TargetPaths(ConversationFile file, ConversationPatch patch, IGameDataProvider provider) → IReadOnlyList<string>`
  - Test helper `FakePoe2Game` (see below)

- [ ] **Step 1: Write the test helper**

```csharp
// DialogEditor.Tests/Helpers/FakePoe2Game.cs
using System.Text.Json;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Helpers;

/// A throwaway PoE2 install on disk: one conversation "test_conv" (node 1, ExternalVO ""),
/// string tables for each requested language, and a VO folder holding "existing.wem".
public sealed class FakePoe2Game : IDisposable
{
    public const string OneNodeBundle = """
        {"Conversations": [{
          "Nodes": [
            {
              "$type": "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats",
              "SpeakerGuid": "aaaa-0000", "ListenerGuid": "bbbb-0000",
              "IsQuestionNode": false, "DisplayType": 0, "Persistence": 0,
              "NodeID": 1, "ContainerNodeID": -1, "Links": [],
              "ClassExtender": {"ExtendedProperties": []},
              "Conditionals": {"Operator": 0, "Components": []},
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
              "HideSpeaker": false, "HasVO": false, "ExternalVO": "",
              "IsTempText": false, "PlayVOAs3DSound": false, "PlayType": 0,
              "NoPlayRandomWeight": 0, "VOPositioning": 0, "NotSkippable": false
            }
          ]
        }]}
        """;

    public static string StringTable(string text) => $"""
        <StringTableFile>
          <Entries>
            <Entry><ID>1</ID><DefaultText>{text}</DefaultText><FemaleText></FemaleText></Entry>
          </Entries>
        </StringTableFile>
        """;

    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"fakepoe2_{Guid.NewGuid():N}");
    public string DataRoot => Path.Combine(Root, "PillarsOfEternityII_Data");
    public string ConvDir  => Path.Combine(DataRoot, "exported", "design", "conversations");
    public string StDir(string lang) => Path.Combine(DataRoot, "exported", "localized", lang, "text", "conversations");
    public string VoDir    => Path.Combine(DataRoot, "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
    public string ConvPath(string name = "test_conv") => Path.Combine(ConvDir, name + ".conversationbundle");
    public string StPath(string lang, string name = "test_conv") => Path.Combine(StDir(lang), name + ".stringtable");

    public FakePoe2Game(params string[] languages)
    {
        if (languages.Length == 0) languages = ["en"];
        Directory.CreateDirectory(ConvDir);
        File.WriteAllText(ConvPath(), OneNodeBundle);
        foreach (var lang in languages)
        {
            Directory.CreateDirectory(StDir(lang));
            File.WriteAllText(StPath(lang), StringTable($"vanilla {lang}"));
        }
        Directory.CreateDirectory(VoDir);
        File.WriteAllBytes(Path.Combine(VoDir, "existing.wem"), [1, 2, 3]);
    }

    public IGameDataProvider Provider => new Poe2GameDataProvider(Root);

    /// Every file under the game's data folder (not the patcher backup), keyed by relative path.
    public SortedDictionary<string, string> SnapshotGameData() =>
        new(Directory.EnumerateFiles(DataRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(DataRoot, f),
                          f => Convert.ToHexString(File.ReadAllBytes(f))));

    public string ReadExternalVo(string name = "test_conv")
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ConvPath(name)));
        return doc.RootElement.GetProperty("Conversations")[0].GetProperty("Nodes")[0]
                  .GetProperty("ExternalVO").GetString()!;
    }

    public void Dispose() { try { Directory.Delete(Root, true); } catch (Exception) { /* best-effort */ } }

    // ── Mod builders ─────────────────────────────────────────────────────

    /// A mod that sets node 1's ExternalVO on test_conv to <paramref name="value"/>.
    public static DialogProject ExternalVoMod(string modName, string value)
    {
        var mod = new NodeModification(1,
            new Dictionary<string, FieldChange>
            {
                ["ExternalVO"] = new(JsonSerializer.Serialize(""), JsonSerializer.Serialize(value)),
            }, [], []);
        return DialogProject.Empty(modName)
            .WithPatch(new ConversationPatch("test_conv", ConversationPatch.CurrentSchemaVersion, [], [], [mod]));
    }
}
```

- [ ] **Step 2: Write the failing tests (the red proof of stacking comes first)**

```csharp
// DialogEditor.Tests/Patch/Install/PatchInstallerTests.cs
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Install;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Patch.Install;

public class PatchInstallerTests
{
    private static InstallResult Install(FakePoe2Game g, params DialogProject[] mods) =>
        PatchInstaller.Install(g.Provider, g.Root, mods.Select(m => new InstallEntry(m)).ToList(), new InstallOptions());

    [Fact]
    public void Install_SameModTwice_DoesNotStack()
    {
        // Issue #76: before the clean-base apply, the second run patched already-modded
        // files and failed with PatchConflictException (ExternalVO was already "a").
        using var game = new FakePoe2Game();
        var mod = FakePoe2Game.ExternalVoMod("A", "a");

        Install(game, mod);
        var result = Install(game, mod);

        Assert.IsType<InstallResult.Applied>(result);
        Assert.Equal("a", game.ReadExternalVo());
    }

    [Fact]
    public void Install_ReorderedLoadOrder_MatchesFreshApply()
    {
        var a = FakePoe2Game.ExternalVoMod("A", "a");
        var b = FakePoe2Game.ExternalVoMod("B", "b");
        using var reapplied = new FakePoe2Game();
        using var fresh     = new FakePoe2Game();

        Install(reapplied, a, b);
        Install(reapplied, b, a);
        Install(fresh, b, a);

        Assert.Equal("a", reapplied.ReadExternalVo());
        Assert.Equal(fresh.SnapshotGameData(), reapplied.SnapshotGameData());
    }

    [Fact]
    public void Install_RemovingModFromList_RevertsIt()
    {
        var a = FakePoe2Game.ExternalVoMod("A", "a");
        var b = FakePoe2Game.ExternalVoMod("B", "b");
        using var reapplied = new FakePoe2Game();
        using var fresh     = new FakePoe2Game();

        Install(reapplied, a, b);
        Install(reapplied, a);
        Install(fresh, a);

        Assert.Equal(fresh.SnapshotGameData(), reapplied.SnapshotGameData());
    }

    [Fact]
    public void Install_WritesTranslationsForEveryInstalledLanguage()
    {
        // The Patch Manager used to drop translations; the shared installer must not.
        using var game = new FakePoe2Game("en", "fr");
        var patch = new ConversationPatch("test_conv", ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(1, "modded en", "")],
                ["fr"] = [new NodeTranslation(1, "modded fr", "")],
            },
        };

        Install(game, DialogProject.Empty("T").WithPatch(patch));

        Assert.Contains("modded en", File.ReadAllText(game.StPath("en")));
        Assert.Contains("modded fr", File.ReadAllText(game.StPath("fr")));
    }

    [Fact]
    public void Install_NewConversation_IsCreated()
    {
        using var game = new FakePoe2Game();
        var added = new NodeEditSnapshot(1, false, SpeakerCategory.Npc, "spk", "lst",
            "", "", "Conversation", "None", "", "", "", false, false, [], [], []);
        var project = DialogProject.Empty("N")
            .WithNewConversation("brand_new")
            .WithPatch(new ConversationPatch("brand_new", ConversationPatch.CurrentSchemaVersion, [added], [], []));

        var result = Assert.IsType<InstallResult.Applied>(Install(game, project));

        Assert.Equal(1, result.ConversationsPatched);
        Assert.True(File.Exists(game.ConvPath("brand_new")));
    }
}
```

- [ ] **Step 3: Add `TranslationApplier.TargetPaths`**

```csharp
    /// The string-table paths WriteTranslations will write for this patch — the installer
    /// backs each one up before the write. Must stay in step with WriteTranslations' filter.
    public static IReadOnlyList<string> TargetPaths(
        ConversationFile file, ConversationPatch patch, IGameDataProvider provider)
    {
        var installed = provider.AvailableLanguages.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return patch.Translations.Keys
            .Where(installed.Contains)
            .Select(lang => provider.GetStringTablePath(file, lang))
            .ToList();
    }
```

- [ ] **Step 4: Write the models and a first installer *without* restore, to see the bug**

```csharp
// DialogEditor.Patch/Install/InstallModels.cs
namespace DialogEditor.Patch.Install;

/// One load-order entry: a project, plus the extracted vo/ folder when it came from a .dialogpack.
public sealed record InstallEntry(DialogProject Project, string? VoFolder = null);

public sealed record InstallOptions(bool Force = false, bool AcceptCurrentFiles = false)
{
    /// Runs before every game-file write with the absolute path — a test seam for
    /// injecting a failure partway through an install.
    public Action<string>? BeforeWrite { get; init; }
}

public abstract record InstallResult
{
    private InstallResult() { }

    public sealed record Applied(
        int                   ConversationsPatched,
        IReadOnlyList<string> MissingConversations,
        IReadOnlyList<string> RestoreSkipped,
        int                   VoFilesCopied) : InstallResult;

    /// Nothing was written: these managed files (relative paths) were changed outside the
    /// patcher, and the caller did not set AcceptCurrentFiles.
    public sealed record ExternalChanges(IReadOnlyList<string> Paths) : InstallResult;
}
```

```csharp
// DialogEditor.Patch/Install/PatchInstaller.cs  (first cut: ported CLI loop, no restore yet)
using DialogEditor.Core.GameData;

namespace DialogEditor.Patch.Install;

public static class PatchInstaller
{
    public static InstallResult Install(
        IGameDataProvider provider, string gameDir,
        IReadOnlyList<InstallEntry> loadOrder, InstallOptions options)
    {
        var merged  = Merge(loadOrder);
        var missing = new List<string>();
        var patched = 0;
        foreach (var name in merged.Patches.Keys.Order())
        {
            var patch = merged.Patches[name];
            var file  = provider.FindConversation(name)
                     ?? (merged.IsNewConversation(name) ? provider.BuildNewConversationFile(name) : null);
            if (file is null) { missing.Add(name); continue; }

            if (!File.Exists(file.ConversationPath)) provider.InitializeConversationFile(file);
            var baseSnap = ConversationSnapshotBuilder.Build(provider.LoadConversation(file));
            provider.SaveConversation(file, PatchApplier.Apply(baseSnap, patch, options.Force));
            TranslationApplier.WriteTranslations(file, patch, provider);
            patched++;
        }
        return new InstallResult.Applied(patched, missing, [], 0);
    }

    private static DialogProject Merge(IReadOnlyList<InstallEntry> loadOrder)
    {
        if (loadOrder.Count == 0) return DialogProject.Empty(string.Empty);
        var merged = loadOrder[0].Project;
        for (var i = 1; i < loadOrder.Count; i++)
            merged = merged.MergeWith(loadOrder[i].Project);
        return merged;
    }
}
```

- [ ] **Step 5: Run the tests — confirm the stacking bug is real**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatchInstallerTests"`
Expected: `Install_SameModTwice_DoesNotStack`, `Install_ReorderedLoadOrder_MatchesFreshApply` and `Install_RemovingModFromList_RevertsIt` FAIL (`PatchConflictException` for the first two, a snapshot mismatch for the third). The translation and new-conversation tests PASS. Record this output in the PR description as the red proof for #76.

- [ ] **Step 6: Add backup + restore to the installer**

Replace the body of `Install` and add the helpers:

```csharp
    public static InstallResult Install(
        IGameDataProvider provider, string gameDir,
        IReadOnlyList<InstallEntry> loadOrder, InstallOptions options)
    {
        var store = PatcherBackupStore.Open(gameDir);

        var changed = store.FindExternallyChanged();
        if (changed.Count > 0)
        {
            if (!options.AcceptCurrentFiles) return new InstallResult.ExternalChanges(changed);
            store.AcceptCurrentAsOriginal(changed);
        }

        // Every apply starts from the originals: the result depends only on the load order,
        // never on what was applied before (issue #76).
        var restoreSkipped = store.RestoreAll();

        var merged  = Merge(loadOrder);
        var missing = new List<string>();
        var patched = 0;
        foreach (var name in merged.Patches.Keys.Order())
        {
            var patch = merged.Patches[name];
            var file  = provider.FindConversation(name)
                     ?? (merged.IsNewConversation(name) ? provider.BuildNewConversationFile(name) : null);
            if (file is null) { missing.Add(name); continue; }

            var targets = WithSidecars([file.ConversationPath, .. TranslationApplier.TargetPaths(file, patch, provider)]);
            foreach (var t in targets) store.EnsureBackedUp(t);
            foreach (var t in targets) options.BeforeWrite?.Invoke(t);

            if (!File.Exists(file.ConversationPath)) provider.InitializeConversationFile(file);
            var baseSnap = ConversationSnapshotBuilder.Build(provider.LoadConversation(file));
            provider.SaveConversation(file, PatchApplier.Apply(baseSnap, patch, options.Force));
            TranslationApplier.WriteTranslations(file, patch, provider);

            foreach (var t in targets.Where(File.Exists)) store.RecordWritten(t);
            patched++;
        }
        return new InstallResult.Applied(patched, missing, restoreSkipped, 0);
    }

    /// The conversation and string-table serializers copy the previous file to "<path>.bak"
    /// before overwriting it, so each sidecar is a write target too — otherwise restore would
    /// leave stray .bak files in the game folder.
    private static List<string> WithSidecars(IEnumerable<string> paths) =>
        paths.SelectMany(p => new[] { p, p + ".bak" }).ToList();
```

Remember: `RecordWritten` only runs for targets that exist after the write. A `.bak` sidecar is only written when the target already existed.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatchInstallerTests|FullyQualifiedName~TranslationApplier"`
Expected: PASS (all 5 installer tests plus the existing TranslationApplier tests).

- [ ] **Step 8: Commit**

```bash
git add DialogEditor.Patch DialogEditor.Tests/Helpers/FakePoe2Game.cs DialogEditor.Tests/Patch/Install/PatchInstallerTests.cs
git commit -m "feat(patch): PatchInstaller applies every load order onto the original files (#76)"
```

---

### Task 4: Installer — VO, external changes, conflicts, crash safety, restore, dry run

**Files:**
- Modify: `DialogEditor.Patch/Install/PatchInstaller.cs`, `DialogEditor.Patch/Install/InstallModels.cs`
- Test: `DialogEditor.Tests/Patch/Install/PatchInstallerSafetyTests.cs`

**Interfaces:**
- Consumes: Task 3.
- Produces:
  - `sealed record RestoreResult(int Restored, IReadOnlyList<string> Skipped)`
  - `sealed record InstallPlan(int ConversationsToPatch, int FilesToRestoreFirst, IReadOnlyList<string> ExternallyChanged)`
  - `PatchInstaller.Restore(string gameDir) → RestoreResult`: after a restore with nothing skipped it deletes the backup folder, so the game folder is clean
  - `PatchInstaller.Plan(string gameDir, IReadOnlyList<InstallEntry> loadOrder) → InstallPlan`
  - `PatchInstaller.VoRoot(IGameDataProvider provider, string gameDir) → string?`: PoE2 only, otherwise null
  - `PatchInstaller.HasInstalledMods(string gameDir) → bool` (= `PatcherBackupStore.Exists`)

- [ ] **Step 1: Write the failing tests**

```csharp
// DialogEditor.Tests/Patch/Install/PatchInstallerSafetyTests.cs
using DialogEditor.Patch;
using DialogEditor.Patch.Install;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Patch.Install;

public class PatchInstallerSafetyTests
{
    private static InstallResult Install(FakePoe2Game g, InstallOptions o, params InstallEntry[] e) =>
        PatchInstaller.Install(g.Provider, g.Root, e, o);

    private static InstallEntry Entry(DialogProject p, string? vo = null) => new(p, vo);

    [Fact]
    public void Restore_GivesByteIdenticalOriginals_AndRemovesBackupFolder()
    {
        using var game = new FakePoe2Game("en", "fr");
        var before = game.SnapshotGameData();
        var vo = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllBytes(Path.Combine(vo, "existing.wem"), [9, 9]);   // overwrites a vanilla file
        File.WriteAllBytes(Path.Combine(vo, "added.wem"),    [7]);      // creates a new one
        try
        {
            var r = Assert.IsType<InstallResult.Applied>(
                Install(game, new InstallOptions(), Entry(FakePoe2Game.ExternalVoMod("A", "a"), vo)));
            Assert.Equal(2, r.VoFilesCopied);
            Assert.NotEqual(before, game.SnapshotGameData());

            var restore = PatchInstaller.Restore(game.Root);

            Assert.Empty(restore.Skipped);
            Assert.Equal(before, game.SnapshotGameData());
            Assert.False(PatchInstaller.HasInstalledMods(game.Root));
        }
        finally { Directory.Delete(vo, true); }
    }

    [Fact]
    public void Install_ExternallyChangedFile_StopsWithoutWriting()
    {
        using var game = new FakePoe2Game();
        Install(game, new InstallOptions(), Entry(FakePoe2Game.ExternalVoMod("A", "a")));
        File.WriteAllText(game.ConvPath(), FakePoe2Game.OneNodeBundle.Replace("aaaa-0000", "game-update"));
        var before = game.SnapshotGameData();

        var result = Install(game, new InstallOptions(), Entry(FakePoe2Game.ExternalVoMod("B", "b")));

        var ext = Assert.IsType<InstallResult.ExternalChanges>(result);
        Assert.Contains(ext.Paths, p => p.EndsWith("test_conv.conversationbundle"));
        Assert.Equal(before, game.SnapshotGameData());
    }

    [Fact]
    public void Install_AcceptCurrentFiles_AdoptsThemAndApplies()
    {
        using var game = new FakePoe2Game();
        Install(game, new InstallOptions(), Entry(FakePoe2Game.ExternalVoMod("A", "a")));
        var updated = FakePoe2Game.OneNodeBundle.Replace("aaaa-0000", "game-update");
        File.WriteAllText(game.ConvPath(), updated);

        var result = Install(game, new InstallOptions(AcceptCurrentFiles: true),
                             Entry(FakePoe2Game.ExternalVoMod("B", "b")));

        Assert.IsType<InstallResult.Applied>(result);
        Assert.Equal("b", game.ReadExternalVo());
        PatchInstaller.Restore(game.Root);
        Assert.Equal(updated, File.ReadAllText(game.ConvPath()));
    }

    [Fact]
    public void Install_PatchConflict_LeavesGameClean()
    {
        using var game = new FakePoe2Game();
        var before = game.SnapshotGameData();
        // Expects ExternalVO "zzz" but the game holds "" → conflict.
        var conflicting = DialogProject.Empty("Bad").WithPatch(new ConversationPatch("test_conv",
            ConversationPatch.CurrentSchemaVersion, [], [],
            [new NodeModification(1, new Dictionary<string, FieldChange>
                { ["ExternalVO"] = new("\"zzz\"", "\"x\"") }, [], [])]));

        Assert.Throws<PatchConflictException>(() =>
            Install(game, new InstallOptions(), Entry(conflicting)));

        Assert.Equal(before, game.SnapshotGameData());
    }

    [Fact]
    public void Install_CrashPartway_ManifestCoversEverythingAndRestoreRecovers()
    {
        using var game = new FakePoe2Game("en", "fr");
        var before = game.SnapshotGameData();
        var vo = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllBytes(Path.Combine(vo, "added.wem"), [7]);
        var writes = 0;
        var options = new InstallOptions
        {
            BeforeWrite = path => { if (++writes == 3) throw new IOException("disk full (simulated)"); },
        };
        try
        {
            Assert.Throws<IOException>(() =>
                Install(game, options, Entry(FakePoe2Game.ExternalVoMod("A", "a"), vo)));

            PatchInstaller.Restore(game.Root);
            Assert.Equal(before, game.SnapshotGameData());
        }
        finally { Directory.Delete(vo, true); }
    }

    [Fact]
    public void Install_And_Restore_RefuseCorruptManifest()
    {
        using var game = new FakePoe2Game();
        var manifest = Path.Combine(game.Root, "PillarsDialogPatcher", "manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllText(manifest, "garbage");

        Assert.Throws<PatcherBackupCorruptException>(() =>
            Install(game, new InstallOptions(), Entry(FakePoe2Game.ExternalVoMod("A", "a"))));
        Assert.Throws<PatcherBackupCorruptException>(() => PatchInstaller.Restore(game.Root));
        Assert.Equal("garbage", File.ReadAllText(manifest));
    }

    [Fact]
    public void Plan_ReportsWorkWithoutWriting()
    {
        using var game = new FakePoe2Game();
        Install(game, new InstallOptions(), Entry(FakePoe2Game.ExternalVoMod("A", "a")));
        var before = game.SnapshotGameData();

        var plan = PatchInstaller.Plan(game.Root, [Entry(FakePoe2Game.ExternalVoMod("B", "b"))]);

        Assert.Equal(1, plan.ConversationsToPatch);
        Assert.True(plan.FilesToRestoreFirst > 0);
        Assert.Empty(plan.ExternallyChanged);
        Assert.Equal(before, game.SnapshotGameData());
    }

    [Fact]
    public void VoRoot_IsNullForPoe1()
    {
        var poe1 = new DialogEditor.Core.GameData.Poe1GameDataProvider(@"C:\x");
        Assert.Null(PatchInstaller.VoRoot(poe1, @"C:\x"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatchInstallerSafetyTests"`
Expected: build FAIL — `Restore`, `Plan`, `VoRoot`, `HasInstalledMods` and `RestoreResult` are not defined.

- [ ] **Step 3: Implement**

Add to `InstallModels.cs`:

```csharp
public sealed record RestoreResult(int Restored, IReadOnlyList<string> Skipped);

public sealed record InstallPlan(
    int                   ConversationsToPatch,
    int                   FilesToRestoreFirst,
    IReadOnlyList<string> ExternallyChanged);
```

In `PatchInstaller`:

1. Wrap the conversation loop plus the VO copy in `try { … } catch (PatchConflictException) { store.RestoreAll(); throw; }`. This leaves the game clean before the caller reports the conflict. This is not a swallow: it rethrows, and the caller logs.
2. After the conversation loop, copy VO:

```csharp
        var voCopied = 0;
        var voRoot   = VoRoot(provider, gameDir);
        if (voRoot is not null)
        {
            foreach (var entry in loadOrder.Where(e => e.VoFolder is not null))
            {
                foreach (var src in Directory.EnumerateFiles(entry.VoFolder!, "*.wem", SearchOption.AllDirectories))
                {
                    var dest = Path.Combine(voRoot, Path.GetRelativePath(entry.VoFolder!, src));
                    store.EnsureBackedUp(dest);
                    options.BeforeWrite?.Invoke(dest);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(src, dest, overwrite: true);
                    store.RecordWritten(dest);
                    voCopied++;
                }
            }
        }
```

   Return `new InstallResult.Applied(patched, missing, restoreSkipped, voCopied)`.
3. Add:

```csharp
    /// PoE2 only: PoE1 keeps VO inside Unity asset archives the patcher cannot write (#4).
    /// The single home of this path; the CLI and the Patch Manager used to hard-code it separately.
    public static string? VoRoot(IGameDataProvider provider, string gameDir) =>
        provider.GameId == "poe2"
            ? Path.Combine(gameDir, "PillarsOfEternityII_Data", "StreamingAssets",
                           "Audio", "Windows", "Voices", "English(US)")
            : null;

    public static bool HasInstalledMods(string gameDir) => PatcherBackupStore.Exists(gameDir);

    /// "Remove all mods". A clean restore also deletes the backup folder, so the game folder
    /// ends up exactly as the patcher found it; if anything was skipped the backup is kept,
    /// because it still holds originals the user may want back once the conflict is resolved.
    public static RestoreResult Restore(string gameDir)
    {
        var store   = PatcherBackupStore.Open(gameDir);
        var total   = store.Entries.Count;
        var skipped = store.RestoreAll();
        if (skipped.Count == 0) store.DeleteBackup();
        return new RestoreResult(total - skipped.Count, skipped);
    }

    public static InstallPlan Plan(string gameDir, IReadOnlyList<InstallEntry> loadOrder)
    {
        var store = PatcherBackupStore.Open(gameDir);
        return new InstallPlan(
            Merge(loadOrder).Patches.Count,
            store.Entries.Count,
            store.FindExternallyChanged());
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Install"`
Expected: PASS (Tasks 1–4). If the crash test's third `BeforeWrite` falls on a path whose backup isn't yet in the manifest, the invariant is broken. Fix the order (all `EnsureBackedUp` calls precede all `BeforeWrite` calls for that conversation), not the test.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/Install DialogEditor.Tests/Patch/Install/PatchInstallerSafetyTests.cs
git commit -m "feat(patch): VO backup, external-change gate, conflict-safe restore and dry-run plan (#76)"
```

---

### Task 5: CLI — `PatcherCommand`, `--restore`, `--accept-current-files`, exit code 3

**Files:**
- Create: `DialogEditor.PatchCli/PatcherCommand.cs`
- Modify: `DialogEditor.PatchCli/Program.cs`
- Modify: `DialogEditor.Tests/DialogEditor.Tests.csproj` (add `<ProjectReference Include="..\DialogEditor.PatchCli\DialogEditor.PatchCli.csproj" />`)
- Test: `DialogEditor.Tests/PatchCli/PatcherCommandTests.cs`

**Interfaces:**
- Consumes: `PatchInstaller.Install/Restore/Plan`, `InstallResult`, `PatcherBackupCorruptException`, `ConflictDetector`, `CrossModConflictReport`, `DialogPackHelper`.
- Produces: `public static class PatcherCommand { public static int Run(string[] args, TextWriter stdout, TextWriter stderr); }`

- [ ] **Step 1: Write the failing tests**

```csharp
// DialogEditor.Tests/PatchCli/PatcherCommandTests.cs
using DialogEditor.Patch;
using DialogEditor.PatchCli;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.PatchCli;

public class PatcherCommandTests : IDisposable
{
    private readonly FakePoe2Game _game = new();
    private readonly string _projDir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose()
    {
        _game.Dispose();
        try { Directory.Delete(_projDir, true); } catch (Exception) { /* best-effort */ }
    }

    private string SaveProject(DialogProject p)
    {
        var path = Path.Combine(_projDir, p.Name + ".dialogproject");
        DialogProjectSerializer.SaveToFile(path, p);
        return path;
    }

    private (int Code, string Out, string Err) Run(params string[] args)
    {
        var o = new StringWriter(); var e = new StringWriter();
        var code = PatcherCommand.Run(args, o, e);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void Apply_ThenApplyAgain_Succeeds()
    {
        var a = SaveProject(FakePoe2Game.ExternalVoMod("A", "a"));
        Assert.Equal(0, Run(_game.Root, a).Code);
        Assert.Equal(0, Run(_game.Root, a).Code);
    }

    [Fact]
    public void Restore_PutsOriginalsBack()
    {
        var before = _game.SnapshotGameData();
        Run(_game.Root, SaveProject(FakePoe2Game.ExternalVoMod("A", "a")));

        var (code, output, _) = Run(_game.Root, "--restore");

        Assert.Equal(0, code);
        Assert.Equal(before, _game.SnapshotGameData());
        Assert.Contains("Restored", output);
    }

    [Fact]
    public void ExternalChange_ExitsWith3_AndNamesTheFlag()
    {
        var a = SaveProject(FakePoe2Game.ExternalVoMod("A", "a"));
        Run(_game.Root, a);
        File.WriteAllText(_game.ConvPath(), FakePoe2Game.OneNodeBundle.Replace("aaaa-0000", "upd"));

        var (code, _, err) = Run(_game.Root, a);

        Assert.Equal(3, code);
        Assert.Contains("test_conv.conversationbundle", err);
        Assert.Contains("--accept-current-files", err);
        Assert.Equal(0, Run(_game.Root, a, "--accept-current-files").Code);
    }

    [Fact]
    public void CorruptManifest_ExitsWith2()
    {
        var dir = Path.Combine(_game.Root, "PillarsDialogPatcher");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), "garbage");

        Assert.Equal(2, Run(_game.Root, "--restore").Code);
    }

    [Fact]
    public void Help_DocumentsRestoreAndExitCode3()
    {
        var (code, output, _) = Run("--help");
        Assert.Equal(0, code);
        Assert.Contains("--restore", output);
        Assert.Contains("--accept-current-files", output);
        Assert.Contains("3 ", output);
    }

    [Fact]
    public void DryRun_ReportsFilesToRestoreFirst()
    {
        var a = SaveProject(FakePoe2Game.ExternalVoMod("A", "a"));
        Run(_game.Root, a);
        var (code, output, _) = Run(_game.Root, a, "--dry-run");
        Assert.Equal(0, code);
        Assert.Contains("restore", output, StringComparison.OrdinalIgnoreCase);
    }
}
```

Before writing this, check `DialogProjectSerializer` for the save method name (`grep -n "public static" DialogEditor.Patch/DialogProjectSerializer.cs`) and use whatever it actually is.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatcherCommandTests"`
Expected: build FAIL — `DialogEditor.PatchCli.PatcherCommand` does not exist.

- [ ] **Step 3: Implement**

Move the whole of `Program.cs` into `PatcherCommand.Run`:
- `Console.WriteLine` becomes `stdout.WriteLine`, `Console.Error.WriteLine` becomes `stderr.WriteLine`, and every `return` stays a `return`. Mark the class `[NotLocalised("dialog-patcher is English-only console output, like CrossModConflictReport")]`.
- `Program.cs` becomes:

```csharp
using DialogEditor.PatchCli;
return PatcherCommand.Run(args, Console.Out, Console.Error);
```

Changes inside `Run` compared with the old program:

1. Help text: the Usage section adds `dialog-patcher <game-dir> --restore`. The Options section adds:
   ```
    --restore               Undo every change dialog-patcher and the Patch Manager have
                            made to this game folder: original files are put back and
                            added files are deleted. No project arguments needed.
    --accept-current-files  Some files the patcher manages were changed by something
                            else (usually a game update). Treat their current contents
                            as the new originals, then apply.
   ```
   Exit codes add `3   Files the patcher manages were changed outside it. Re-run with --accept-current-files, or --restore.` Also add a paragraph after Arguments: `Every apply first restores the original game files, then applies the whole load order, so re-running with a different list replaces the previous mods.`
2. Parse `bool restore = Has("--restore"); bool acceptCurrent = Has("--accept-current-files");`. Minimum positionals: 1 when `restore`, else 2.
3. After game detection, when `restore` is set:

```csharp
try
{
    var r = PatchInstaller.Restore(gameDir);
    Info($"Restored {r.Restored} file(s).");
    foreach (var p in r.Skipped)
        stderr.WriteLine($"Warning: left as is (changed outside the patcher, probably by a game update): {p}");
    return 0;
}
catch (PatcherBackupCorruptException ex) { return CorruptManifest(ex); }
```

4. Load projects exactly as before, but collect `List<InstallEntry>` (`new InstallEntry(project, voFolder)`). Keep the #6 cross-mod warning block unchanged.
5. Dry run: `var plan = PatchInstaller.Plan(gameDir, entries);` then `Info($"Dry run: would restore {plan.FilesToRestoreFirst} file(s) first, then patch {plan.ConversationsToPatch} conversation(s).");`. If `plan.ExternallyChanged.Count > 0`, add a warning listing them. Return 0.
6. Apply:

```csharp
InstallResult result;
try
{
    result = PatchInstaller.Install(provider, gameDir, entries,
        new InstallOptions(Force: force, AcceptCurrentFiles: acceptCurrent));
}
catch (PatcherBackupCorruptException ex) { CleanupTempDirs(tempDirs); return CorruptManifest(ex); }
catch (PatchConflictException ex)
{
    CleanupTempDirs(tempDirs);
    AppLog.Error("dialog-patcher: patch conflict", ex);
    // (existing conflict report lines, unchanged)
    stderr.WriteLine("The game files were restored to their originals; nothing is half-applied.");
    stderr.WriteLine("Re-run with --force to apply the patch's target value anyway.");
    return 1;
}
catch (Exception ex)
{
    CleanupTempDirs(tempDirs);
    AppLog.Error("dialog-patcher: install failed", ex);
    Error($"Unexpected error: {ex.Message}");
    stderr.WriteLine("Run 'dialog-patcher <game-dir> --restore' to return to the original files.");
    if (verbose) stderr.WriteLine(ex.ToString());
    return 2;
}
CleanupTempDirs(tempDirs);

switch (result)
{
    case InstallResult.ExternalChanges ext:
        stderr.WriteLine("These files were changed outside the patcher (probably by a game update):");
        foreach (var p in ext.Paths) stderr.WriteLine($"  {p}");
        stderr.WriteLine("Nothing was written. Re-run with --accept-current-files to treat them as the new");
        stderr.WriteLine("originals, or with --restore to undo the patcher's other changes first.");
        return 3;
    case InstallResult.Applied a:
        foreach (var m in a.MissingConversations)
            stderr.WriteLine($"Warning: conversation not found on disk, skipping: {m}");
        foreach (var p in a.RestoreSkipped)
            stderr.WriteLine($"Warning: left as is (changed outside the patcher): {p}");
        Info(a.MissingConversations.Count > 0
            ? $"Done: {a.ConversationsPatched} patched, {a.MissingConversations.Count} skipped (conversation not found)."
            : $"Done: {a.ConversationsPatched} conversation(s) patched successfully.");
        if (a.VoFilesCopied > 0) Info($"Copied {a.VoFilesCopied} VO file(s).");
        return 0;
}
return 2;

int CorruptManifest(PatcherBackupCorruptException ex)
{
    AppLog.Error("dialog-patcher: backup manifest unreadable", ex);
    Error($"The patcher's backup record is unreadable: {ex.ManifestPath}");
    stderr.WriteLine("Nothing was changed. Use your storefront's 'verify game files' to get clean originals,");
    stderr.WriteLine("then delete the PillarsDialogPatcher folder in the game directory.");
    return 2;
}
```

Delete the old hard-coded VO block and the old apply loop: the installer does both now. Drop the "Nothing to apply" early return. An empty merged project still restores, which is what the user wants when every mod has been removed from the list.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatcherCommandTests|FullyQualifiedName~Install"`
Expected: PASS. Then `dotnet build DialogEditor.PatchCli` and a manual `dotnet run --project DialogEditor.PatchCli -- --help`, and check that the help reads well.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.PatchCli DialogEditor.Tests/PatchCli DialogEditor.Tests/DialogEditor.Tests.csproj
git commit -m "feat(patch-cli): --restore, --accept-current-files and exit code 3 via PatchInstaller (#76)"
```

---

### Task 6: Patch Manager — apply via installer, Remove all mods, external-change prompt

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/PatchManagerViewModel.cs`
- Modify: `DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml`
- Modify: `DialogEditor.Avalonia.Shared/PatchManagerView.axaml`
- Create: `DialogEditor.Avalonia.Shared/ConfirmDialog.axaml`, `ConfirmDialog.axaml.cs`
- Create: `DialogEditor.Avalonia.Shared/PatchManagerDialogs.cs`
- Modify: `DialogEditor.PatchManager/MainWindow.axaml.cs`, `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (in `PatchManager_Click`)
- Test: `DialogEditor.Tests/ViewModels/PatchManagerViewModelInstallTests.cs`

**Interfaces:**
- Consumes: `PatchInstaller.Install/Restore/HasInstalledMods`, `InstallResult`, `RestoreResult`, `PatcherBackupStore.Open(...).Entries.Count`, `PatcherBackupCorruptException`.
- Produces (VM):
  - `Func<IReadOnlyList<string>, Task<bool>>? ConfirmAcceptExternalChanges { get; set; }`: true = treat as new originals
  - `Func<Task<bool>>? ConfirmRemoveAllMods { get; set; }`
  - `[ObservableProperty] bool _hasInstalledMods`, `[ObservableProperty] string _backupStatusText`
  - `IAsyncRelayCommand RemoveAllModsCommand` (CanExecute: `HasInstalledMods && !IsApplying`)
  - `void RefreshBackupStatus()`: called when `GameFolder` changes and after apply/restore
- Produces (view): `ConfirmDialog(string title, string message, string confirmText, IReadOnlyList<string>? details)` with `Task<bool> ShowAsync(Window owner)`; `PatchManagerDialogs.Attach(PatchManagerViewModel vm, Window owner)`

- [ ] **Step 1: Write the failing VM tests**

```csharp
// DialogEditor.Tests/ViewModels/PatchManagerViewModelInstallTests.cs
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class PatchManagerViewModelInstallTests : IDisposable
{
    private readonly FakePoe2Game _game = new("en", "fr");

    public PatchManagerViewModelInstallTests() => Loc.Configure(new StubStringProvider());
    public void Dispose() => _game.Dispose();

    private PatchManagerViewModel MakeVm(params DialogProject[] mods)
    {
        var vm = new PatchManagerViewModel(new StubFolderPicker(), new StubFilePicker())
        {
            GameFolder = _game.Root,
        };
        foreach (var m in mods) vm.Entries.Add(new PatchEntryViewModel(m.Name, m));
        return vm;
    }

    [Fact]
    public async Task Apply_Twice_Succeeds_AndEnablesRemoveAll()
    {
        var vm = MakeVm(FakePoe2Game.ExternalVoMod("A", "a"));
        Assert.False(vm.RemoveAllModsCommand.CanExecute(null));

        await vm.ApplyCommand.ExecuteAsync(null);
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("a", _game.ReadExternalVo());
        Assert.True(vm.HasInstalledMods);
        Assert.True(vm.RemoveAllModsCommand.CanExecute(null));
        Assert.StartsWith("PatchManager_ApplySuccess", vm.StatusText);
    }

    [Fact]
    public async Task Apply_WritesTranslations()
    {
        var patch = new ConversationPatch("test_conv", ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<DialogEditor.Core.Models.NodeTranslation>>
            {
                ["fr"] = [new(1, "modded fr", "")],
            },
        };
        var vm = MakeVm(DialogProject.Empty("T").WithPatch(patch));

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.Contains("modded fr", File.ReadAllText(_game.StPath("fr")));
    }

    [Fact]
    public async Task RemoveAll_Confirmed_RestoresOriginals()
    {
        var before = _game.SnapshotGameData();
        var vm = MakeVm(FakePoe2Game.ExternalVoMod("A", "a"));
        vm.ConfirmRemoveAllMods = () => Task.FromResult(true);
        await vm.ApplyCommand.ExecuteAsync(null);

        await vm.RemoveAllModsCommand.ExecuteAsync(null);

        Assert.Equal(before, _game.SnapshotGameData());
        Assert.False(vm.HasInstalledMods);
        Assert.Single(vm.Entries);   // the load order is kept
    }

    [Fact]
    public async Task RemoveAll_Declined_ChangesNothing()
    {
        var vm = MakeVm(FakePoe2Game.ExternalVoMod("A", "a"));
        vm.ConfirmRemoveAllMods = () => Task.FromResult(false);
        await vm.ApplyCommand.ExecuteAsync(null);

        await vm.RemoveAllModsCommand.ExecuteAsync(null);

        Assert.Equal("a", _game.ReadExternalVo());
    }

    [Theory]
    [InlineData(true,  "b")]
    [InlineData(false, "a")]
    public async Task Apply_ExternalChanges_AsksAndHonoursAnswer(bool accept, string expectedVo)
    {
        var vm = MakeVm(FakePoe2Game.ExternalVoMod("A", "a"));
        await vm.ApplyCommand.ExecuteAsync(null);
        // Change a managed file externally: the bundle's ".bak" sidecar (Created by the first
        // apply) — the bundle itself stays ours, so ExternalVO remains readable.
        File.WriteAllText(_game.ConvPath() + ".bak", "someone else");
        IReadOnlyList<string>? asked = null;
        vm.ConfirmAcceptExternalChanges = paths => { asked = paths; return Task.FromResult(accept); };
        vm.Entries.Clear();
        vm.Entries.Add(new PatchEntryViewModel("B", FakePoe2Game.ExternalVoMod("B", "b")));

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.NotNull(asked);
        Assert.Equal(expectedVo, _game.ReadExternalVo());
        if (!accept) Assert.Equal("PatchManager_ApplyCancelledExternal", vm.StatusText);
    }

    [Fact]
    public void GameFolder_WithBackup_ShowsBackupStatus()
    {
        var vm = MakeVm();
        Assert.Equal("PatchManager_NoModsInstalled", vm.BackupStatusText);
    }
}
```

Note on the external-change test: `ExternalVoMod` has no translations, so the only managed files are the bundle and its `.bak` sidecar.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatchManagerViewModelInstallTests"`
Expected: build FAIL — `RemoveAllModsCommand`, `ConfirmRemoveAllMods`, `ConfirmAcceptExternalChanges`, `HasInstalledMods` and `BackupStatusText` are not defined.

- [ ] **Step 3: Implement the VM**

- Delete `ApplyPatches(IGameDataProvider)` and the hard-coded VO root.
- New members:

```csharp
    /// Set by the host view: shows the files changed outside the patcher and returns true
    /// when the user chooses to treat them as the new originals.
    public Func<IReadOnlyList<string>, Task<bool>>? ConfirmAcceptExternalChanges { get; set; }

    /// Set by the host view: returns true when the user confirms "Remove all mods".
    public Func<Task<bool>>? ConfirmRemoveAllMods { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveAllModsCommand))]
    private bool _hasInstalledMods;

    [ObservableProperty] private string _backupStatusText = string.Empty;

    partial void OnGameFolderChanged(string value) => RefreshBackupStatus();

    public void RefreshBackupStatus()
    {
        if (string.IsNullOrEmpty(GameFolder) || !Directory.Exists(GameFolder))
        {
            HasInstalledMods = false;
            BackupStatusText = string.Empty;
            return;
        }
        try
        {
            HasInstalledMods = PatchInstaller.HasInstalledMods(GameFolder);
            BackupStatusText = HasInstalledMods
                ? Loc.FormatCount("PatchManager_ModsInstalled",
                                  PatcherBackupStore.Open(GameFolder).Entries.Count)
                : Loc.Get("PatchManager_NoModsInstalled");
        }
        catch (PatcherBackupCorruptException ex)
        {
            AppLog.Error("Patch Manager: backup manifest unreadable", ex);
            HasInstalledMods = false;
            BackupStatusText = Loc.Format("PatchManager_BackupCorrupt", ex.ManifestPath);
        }
    }
```

  Also add `[NotifyCanExecuteChangedFor(nameof(RemoveAllModsCommand))]` to `_isApplying`.

- New `Apply` body (keep the provider detection and `IsApplying` bookkeeping):

```csharp
        var entries = Entries.Where(e => e.IsLoaded)
                             .Select(e => new InstallEntry(e.Project!, e.VoFolder))
                             .ToList();
        try
        {
            var result = await Task.Run(() => PatchInstaller.Install(provider, GameFolder, entries, new InstallOptions()));
            if (result is InstallResult.ExternalChanges ext)
            {
                var accept = ConfirmAcceptExternalChanges is not null
                          && await ConfirmAcceptExternalChanges(ext.Paths);
                if (!accept)
                {
                    AppLog.Warn($"Apply cancelled: {ext.Paths.Count} file(s) changed outside the patcher");
                    StatusText = Loc.Get("PatchManager_ApplyCancelledExternal");
                    return;
                }
                result = await Task.Run(() => PatchInstaller.Install(provider, GameFolder, entries,
                                                                     new InstallOptions(AcceptCurrentFiles: true)));
            }
            var applied = (InstallResult.Applied)result;
            foreach (var p in applied.RestoreSkipped)
                AppLog.Warn($"Left as is (changed outside the patcher): {p}");
            AppLog.Info($"Applied {applied.ConversationsPatched} conversation(s) from {entries.Count} project(s)");
            StatusText = Loc.FormatCount("PatchManager_ApplySuccess", applied.ConversationsPatched, GameFolder);
        }
        catch (PatcherBackupCorruptException ex)
        {
            AppLog.Error("Patch application refused: backup manifest unreadable", ex);
            StatusText = Loc.Format("PatchManager_BackupCorrupt", ex.ManifestPath);
        }
        catch (Exception ex)
        {
            AppLog.Error("Patch application failed", ex);
            StatusText = Loc.Format("PatchManager_ApplyError", ex.Message);
        }
        finally
        {
            IsApplying = false;
            ApplyCommand.NotifyCanExecuteChanged();
            RefreshBackupStatus();
        }
```

  The `ExternalChanges` branch returns before `finally` has done anything unusual. `finally` still runs: that's intended.

- New command:

```csharp
    [RelayCommand(CanExecute = nameof(CanRemoveAllMods))]
    private async Task RemoveAllMods()
    {
        if (ConfirmRemoveAllMods is null || !await ConfirmRemoveAllMods()) return;
        IsApplying = true;
        try
        {
            var r = await Task.Run(() => PatchInstaller.Restore(GameFolder));
            foreach (var p in r.Skipped)
                AppLog.Warn($"Remove all mods left a file as is (changed outside the patcher): {p}");
            AppLog.Info($"Removed all mods: {r.Restored} file(s) restored, {r.Skipped.Count} skipped");
            StatusText = r.Skipped.Count == 0
                ? Loc.FormatCount("PatchManager_RemoveAllDone", r.Restored)
                : Loc.Format("PatchManager_RemoveAllPartial", r.Restored, r.Skipped.Count);
        }
        catch (PatcherBackupCorruptException ex)
        {
            AppLog.Error("Remove all mods refused: backup manifest unreadable", ex);
            StatusText = Loc.Format("PatchManager_BackupCorrupt", ex.ManifestPath);
        }
        catch (Exception ex)
        {
            AppLog.Error("Remove all mods failed", ex);
            StatusText = Loc.Format("PatchManager_RemoveAllError", ex.Message);
        }
        finally
        {
            IsApplying = false;
            RefreshBackupStatus();
        }
    }

    private bool CanRemoveAllMods() => HasInstalledMods && !IsApplying;
```

- [ ] **Step 4: Add the strings (`SharedStrings.axaml`, beside the other Patch Manager keys)**

```xml
    <sys:String x:Key="PatchManager_RemoveAllMods">Remove all mods</sys:String>
    <sys:String x:Key="PatchManager_NoModsInstalled">No mods installed with the Patch Manager or dialog-patcher yet.</sys:String>
    <sys:String x:Key="PatchManager_ModsInstalled_One">Mods are installed: 1 original game file is backed up.</sys:String>
    <sys:String x:Key="PatchManager_ModsInstalled_Other">Mods are installed: {0} original game files are backed up.</sys:String>
    <sys:String x:Key="PatchManager_BackupCorrupt">The patcher's backup record is unreadable ({0}). Nothing was changed. Verify the game files in your storefront, then delete the PillarsDialogPatcher folder.</sys:String>
    <sys:String x:Key="PatchManager_ApplyCancelledExternal">Apply cancelled — nothing was changed.</sys:String>
    <sys:String x:Key="PatchManager_RemoveAllDone_One">Removed all mods: 1 file restored.</sys:String>
    <sys:String x:Key="PatchManager_RemoveAllDone_Other">Removed all mods: {0} files restored.</sys:String>
    <sys:String x:Key="PatchManager_RemoveAllPartial">Removed mods: {0} files restored; {1} left as is because something else changed them (see the log).</sys:String>
    <sys:String x:Key="PatchManager_RemoveAllError">Remove all mods failed: {0}</sys:String>
    <sys:String x:Key="PatchManager_RemoveAllConfirm_Title">Remove all mods?</sys:String>
    <sys:String x:Key="PatchManager_RemoveAllConfirm_Message">Every game file the Patch Manager or dialog-patcher changed is put back to its original, and the files they added are deleted. Your load order stays in the list, so you can apply it again later.</sys:String>
    <sys:String x:Key="PatchManager_ExternalChanges_Title">Game files changed outside the Patch Manager</sys:String>
    <sys:String x:Key="PatchManager_ExternalChanges_Message">These files were changed by something other than the Patch Manager — usually a game update or "verify files", sometimes another mod tool. Treat their current contents as the new originals and apply your load order on top of them?</sys:String>
    <sys:String x:Key="PatchManager_ExternalChanges_Confirm">Treat as new originals</sys:String>
    <sys:String x:Key="PatchManager_ExternalChanges_More">…and {0} more</sys:String>
    <sys:String x:Key="Confirm_Cancel">Cancel</sys:String>
    <sys:String x:Key="ToolTip_RemoveAllMods">Undo every mod installed into this game folder: restore each game file the patcher changed to its original version and delete the files it added. The load order list is kept.</sys:String>
```

Also update `ToolTip_ApplyPatches` to: `Reset the game to its original files, then apply every patch in load order. Lower entries win on conflict. Removing a project from the list and applying again removes that mod.`

- [ ] **Step 5: Add `ConfirmDialog` + `PatchManagerDialogs`**

`ConfirmDialog.axaml`: a `Window` with `Icon` (copy the icon attribute and resource-dictionary usage from `ThemeOnboardingWindow.axaml`), `SizeToContent="WidthAndHeight"`, `MaxWidth="560"`, `CanResize="False"`, `WindowStartupLocation="CenterOwner"`. It contains:
- a wrapping `TextBlock x:Name="MessageText"`
- an `ItemsControl x:Name="DetailsList"` inside a `ScrollViewer MaxHeight="200"`, hidden when empty
- a right-aligned button row: `ConfirmButton` (class `primary-btn`) and `CancelButton`, with `Content="{DynamicResource Confirm_Cancel}"` and `IsCancel="True"`

The code-behind sets title/message/confirm text, stores the result, and `ShowAsync(owner)` returns `await ShowDialog<bool>(owner)` (Confirm → `Close(true)`, Cancel → `Close(false)`). Add a parameterless constructor for the previewer.

```csharp
// DialogEditor.Avalonia.Shared/PatchManagerDialogs.cs
using Avalonia.Controls;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Shared;

/// Wires PatchManagerViewModel's confirmations to real dialogs. Shared so the editor's
/// Patch Manager window and the standalone app behave identically.
public static class PatchManagerDialogs
{
    private const int MaxListedFiles = 10;

    public static void Attach(PatchManagerViewModel vm, Window owner)
    {
        vm.ConfirmRemoveAllMods = () => new ConfirmDialog(
            Loc.Get("PatchManager_RemoveAllConfirm_Title"),
            Loc.Get("PatchManager_RemoveAllConfirm_Message"),
            Loc.Get("PatchManager_RemoveAllMods"),
            details: null).ShowAsync(owner);

        vm.ConfirmAcceptExternalChanges = paths =>
        {
            var shown = paths.Take(MaxListedFiles).ToList();
            if (paths.Count > MaxListedFiles)
                shown.Add(Loc.Format("PatchManager_ExternalChanges_More", paths.Count - MaxListedFiles));
            return new ConfirmDialog(
                Loc.Get("PatchManager_ExternalChanges_Title"),
                Loc.Get("PatchManager_ExternalChanges_Message"),
                Loc.Get("PatchManager_ExternalChanges_Confirm"),
                shown).ShowAsync(owner);
        };
    }
}
```

Call `PatchManagerDialogs.Attach(vm, this);` in `DialogEditor.PatchManager/MainWindow.axaml.cs` after creating the VM. Call it with `(vm, _patchManagerWindow)` in the editor's `PatchManager_Click` after constructing `PatchManagerWindow`.

- [ ] **Step 6: Update `PatchManagerView.axaml`'s footer**

Change the footer grid to `ColumnDefinitions="*,Auto,Auto"`. Put the new button in column 1:

```xml
            <Button Grid.Column="1" Margin="0,0,8,0"
                    Content="{DynamicResource PatchManager_RemoveAllMods}"
                    Command="{Binding RemoveAllModsCommand}"
                    ToolTip.Tip="{DynamicResource ToolTip_RemoveAllMods}"
                    AutomationProperties.HelpText="{DynamicResource ToolTip_RemoveAllMods}"/>
```

Move both Apply buttons to `Grid.Column="2"`. Directly above the footer, add a status row showing `{Binding BackupStatusText}` (small, muted, wrapping). Add a `RowDefinition` for it and shift the footer's `Grid.Row` accordingly. Add a comment explaining that it tells the player whether "Remove all mods" has anything to undo.

- [ ] **Step 7: Run tests and build**

Run: `dotnet build` then `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: build OK; all tests PASS, including `PatchManagerViewModelTests`, `PatchManagerViewTests`, and both hard-coded-string scanners (XAML + C#).

- [ ] **Step 8: Commit**

```bash
git add DialogEditor.ViewModels DialogEditor.Avalonia.Shared DialogEditor.PatchManager DialogEditor.Avalonia/Views/MainWindow.axaml.cs DialogEditor.Tests/ViewModels/PatchManagerViewModelInstallTests.cs
git commit -m "feat(patch-manager): clean-base apply, Remove all mods and external-change prompt (#76)"
```

---

### Task 7: Editor — warn before Test Patch over patcher-managed files

**Files:**
- Create: `DialogEditor.Patch/Install/PatcherBackupInfo.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (`DoTestPatch`, new callback)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (wire callback)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/Patch/Install/PatcherBackupInfoTests.cs`, `DialogEditor.Tests/ViewModels/MainWindowViewModelTestPatchPatcherTests.cs`

**Interfaces:**
- Consumes: `PatcherBackupStore`, `PatchInstaller.Install` (test setup), `FakePoe2Game`.
- Produces:
  - `sealed class PatcherBackupInfo { static PatcherBackupInfo? TryOpen(string gameDir); bool IsUnknown { get; } IReadOnlyList<string> CoveredPaths(IEnumerable<string> absPaths); }`: `TryOpen` returns null when there is no manifest; a corrupt manifest gives `IsUnknown = true`, and then `CoveredPaths` returns every input
  - `MainWindowViewModel.ConfirmTestOverPatcherMods : Func<int, Task<bool>>?`: the argument is how many of the test's files the patcher manages; true = continue

- [ ] **Step 1: Write the failing tests**

```csharp
// DialogEditor.Tests/Patch/Install/PatcherBackupInfoTests.cs
using DialogEditor.Patch.Install;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Patch.Install;

public class PatcherBackupInfoTests
{
    [Fact]
    public void TryOpen_NoManifest_ReturnsNull()
    {
        using var game = new FakePoe2Game();
        Assert.Null(PatcherBackupInfo.TryOpen(game.Root));
    }

    [Fact]
    public void CoveredPaths_ReturnsOnlyManagedFiles()
    {
        using var game = new FakePoe2Game();
        PatchInstaller.Install(game.Provider, game.Root,
            [new InstallEntry(FakePoe2Game.ExternalVoMod("A", "a"))], new InstallOptions());

        var info = PatcherBackupInfo.TryOpen(game.Root)!;

        Assert.Equal([game.ConvPath()], info.CoveredPaths([game.ConvPath(), game.StPath("en")]));
    }

    [Fact]
    public void CorruptManifest_IsUnknown_AndCoversEverything()
    {
        using var game = new FakePoe2Game();
        var dir = Path.Combine(game.Root, PatcherBackupStore.FolderName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, PatcherBackupStore.ManifestFileName), "garbage");

        var info = PatcherBackupInfo.TryOpen(game.Root)!;

        Assert.True(info.IsUnknown);
        Assert.Equal([game.StPath("en")], info.CoveredPaths([game.StPath("en")]));
    }
}
```

Base the VM test on `MainWindowViewModelTestPatchTests`: copy its constructor/Dispose, `MakeVm`, `Inject` and `InjectProject` helpers and `ProjectWithAddedNode()`, but build the game with `FakePoe2Game` (its `test_conv` matches). Also inject `_currentGameDirectory` = `game.Root`.

```csharp
    [Theory]
    [InlineData(false, false)]   // patcher manages test_conv, user cancels → nothing written
    [InlineData(true,  true)]    // user continues → test patch applied
    public async Task TestPatch_OverPatcherMods_AsksFirst(bool answer, bool expectWritten)
    {
        PatchInstaller.Install(_game.Provider, _game.Root,
            [new InstallEntry(FakePoe2Game.ExternalVoMod("A", "a"))], new InstallOptions());
        var vm = MakeVmWithProjectAndGame();
        int? asked = null;
        vm.ConfirmTestOverPatcherMods = n => { asked = n; return Task.FromResult(answer); };
        var stBefore = File.ReadAllText(_game.StPath("en"));

        await vm.TestPatchCommand.ExecuteAsync(null);

        Assert.True(asked > 0);
        Assert.Equal(expectWritten, File.ReadAllText(_game.StPath("en")).Contains("added line"));
        if (!answer) Assert.Equal(stBefore, File.ReadAllText(_game.StPath("en")));
    }

    [Fact]
    public async Task TestPatch_NoPatcherMods_DoesNotAsk()
    {
        var vm = MakeVmWithProjectAndGame();
        var asked = false;
        vm.ConfirmTestOverPatcherMods = _ => { asked = true; return Task.FromResult(true); };

        await vm.TestPatchCommand.ExecuteAsync(null);

        Assert.False(asked);
    }
```

The added-node project writes node 99 on `test_conv`. Adjust the "added line" assertion to whatever `ProjectWithAddedNode` writes (it's "added line" in the existing test).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatcherBackupInfo|FullyQualifiedName~TestPatchPatcher"`
Expected: build FAIL — `PatcherBackupInfo` and `ConfirmTestOverPatcherMods` are not defined.

- [ ] **Step 3: Implement**

```csharp
// DialogEditor.Patch/Install/PatcherBackupInfo.cs
using DialogEditor.Core.Logging;

namespace DialogEditor.Patch.Install;

/// Read-only view of the patcher's backup for the editor (issue #76): the editor never writes
/// the patcher's manifest, it only asks "are mods installed over these files?". Merging the
/// two backup systems is #66's job.
public sealed class PatcherBackupInfo
{
    private readonly PatcherBackupStore? _store;

    private PatcherBackupInfo(PatcherBackupStore? store) => _store = store;

    /// True when a manifest exists but cannot be read — callers should assume the worst.
    public bool IsUnknown => _store is null;

    public static PatcherBackupInfo? TryOpen(string gameDir)
    {
        if (string.IsNullOrEmpty(gameDir) || !PatcherBackupStore.Exists(gameDir)) return null;
        try
        {
            return new PatcherBackupInfo(PatcherBackupStore.Open(gameDir));
        }
        catch (PatcherBackupCorruptException ex)
        {
            AppLog.Error("Patcher backup manifest unreadable; assuming it covers every file", ex);
            return new PatcherBackupInfo(null);
        }
    }

    public IReadOnlyList<string> CoveredPaths(IEnumerable<string> absPaths)
    {
        if (_store is null) return absPaths.ToList();
        var managed = _store.Entries.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return absPaths.Where(p =>
        {
            try { return managed.Contains(_store.ToRelative(p)); }
            catch (ArgumentException ex)
            {
                AppLog.Warn($"Path outside game folder ignored for patcher check: {p} ({ex.Message})");
                return false;
            }
        }).ToList();
    }
}
```

In `MainWindowViewModel`, next to `RequestConflictResolution`:

```csharp
    /// Set by the UI layer. Asked before Test Patch writes files that mods installed with the
    /// Patch Manager / dialog-patcher also manage (argument: how many). True = continue.
    public Func<int, Task<bool>>? ConfirmTestOverPatcherMods { get; set; }
```

At the top of `DoTestPatch`, after the `Patches.Count == 0` check and only when `!ignoreConflicts` (the forced re-run must not ask twice):

```csharp
        if (!ignoreConflicts && PatcherBackupInfo.TryOpen(_currentGameDirectory) is { } patcher)
        {
            var paths = _project.Patches.Keys
                .Select(name => _provider.FindConversation(name))
                .Where(f => f is not null)
                .SelectMany(f => new[] { f!.ConversationPath, _provider.GetStringTablePath(f) })
                .ToList();
            var covered = patcher.CoveredPaths(paths).Count;
            if (covered > 0 && ConfirmTestOverPatcherMods is not null
                && !await ConfirmTestOverPatcherMods(covered))
            {
                AppLog.Warn($"Test Patch cancelled: {covered} file(s) are managed by the patcher");
                StatusText = Loc.Get("Status_TestPatchCancelledPatcherMods");
                return;
            }
        }
```

Strings (`DialogEditor.Avalonia/Resources/Strings.axaml`, near `Status_ProjectNoPatch`):

```xml
    <sys:String x:Key="Status_TestPatchCancelledPatcherMods">Test cancelled — nothing was changed. Remove the installed mods in the Patch Manager first, then test again.</sys:String>
    <sys:String x:Key="TestOverPatcher_Title">Mods are installed over these files</sys:String>
    <sys:String x:Key="TestOverPatcher_Message">{0} of the files this test writes are managed by mods installed with the Patch Manager or dialog-patcher. Testing now patches the modded versions, and the Patch Manager will ask about these files on its next apply. For a clean test, cancel and use "Remove all mods" in the Patch Manager first.</sys:String>
    <sys:String x:Key="TestOverPatcher_Continue">Test anyway</sys:String>
```

Wire it in `MainWindow.axaml.cs` next to `RequestConflictResolution`:

```csharp
        vm.ConfirmTestOverPatcherMods = count => new DialogEditor.Avalonia.Shared.ConfirmDialog(
            Loc.Get("TestOverPatcher_Title"),
            Loc.Format("TestOverPatcher_Message", count),
            Loc.Get("TestOverPatcher_Continue"),
            details: null).ShowAsync(this);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/Install/PatcherBackupInfo.cs DialogEditor.ViewModels DialogEditor.Avalonia DialogEditor.Tests
git commit -m "feat(editor): warn before Test Patch over patcher-installed mods (#76)"
```

---

### Task 8: End-to-end verification and PR

**Files:** none new, unless verification finds defects.

- [ ] **Step 1: Full headless suite**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: all PASS. Paste the counts into the PR.

- [ ] **Step 2: GUI check (use the `running-the-app` skill)**

Build a scratch fake PoE2 folder (copy `FakePoe2Game`'s layout by hand into the scratchpad) and a `.dialogproject` from the sample project. In the standalone Patch Manager:
1. Pick the folder → the status says "No mods installed…"; Remove all mods is disabled.
2. Add the project → Apply → success; the status says N files backed up; Remove all mods is enabled.
3. Apply again → success (no conflict).
4. Edit a managed file by hand → Apply → the external-changes dialog lists it → Cancel → the status says cancelled.
5. Remove all mods → confirm → the files match the originals (compare hashes), and the `PillarsDialogPatcher` folder is gone.

Check that every new control is found by its UIA name through `tools/ui-automation/DriveApp.ps1` and has a tooltip. Take screenshots of the new button and both dialogs.

- [ ] **Step 3: CLI smoke test**

```bash
dotnet run --project DialogEditor.PatchCli -- --help
```

Then apply → apply → `--restore` against the same scratch folder, and check the exit codes (0, 0, 0) and the messages.

- [ ] **Step 4: Push and open the PR**

Title: `feat(patch): back up originals, apply onto a clean base, Remove all mods (#76)`. The body covers the red proof from Task 3 Step 5, a summary of the design, the test counts, the screenshots, "Closes #76", and the Claude Code footer. Then enable Auto-fix with `set_monitor` (standing instruction).
