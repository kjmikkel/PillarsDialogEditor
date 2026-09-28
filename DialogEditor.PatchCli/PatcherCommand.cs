using System.Reflection;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Localisation;
using DialogEditor.Core.Logging;
using DialogEditor.Patch;
using DialogEditor.Patch.Install;
using DialogEditor.Patch.Packaging;
using DialogEditor.Patch.Schema;

namespace DialogEditor.PatchCli;

/// <summary>
/// dialog-patcher's whole command line, as a method rather than top-level statements so the
/// tests can drive it with captured output. Every apply goes through PatchInstaller, which
/// restores the original game files first (issue #76), so re-running with a different load
/// order replaces the previous mods instead of stacking on them.
/// </summary>
[NotLocalised("dialog-patcher is English-only console output, like CrossModConflictReport")]
public static class PatcherCommand
{
    private const string Help = $"""
      dialog-patcher — apply Pillars of Eternity dialog patch projects to game files

      Usage:
        dialog-patcher <game-dir> <project.dialogproject> [project2.dialogproject ...] [options]
        dialog-patcher <game-dir> --restore

      Arguments:
        game-dir                Root directory of a PoE1 or PoE2 installation.
        project.dialogproject   One or more .dialogproject or .dialogpack files to apply.
                                When multiple projects are given they are merged in
                                order (later projects win on any contested field).
                                Every such overlap is listed as a warning before the
                                apply; use --dry-run to check a load order first.

      Every apply first restores the original game files, then applies the whole load
      order, so re-running with a different list replaces the previous mods. Originals
      are kept in the PillarsDialogPatcher folder inside the game directory.

      Options:
        --restore               Undo every change dialog-patcher and the Patch Manager have
                                made to this game folder: original files are put back and
                                added files are deleted. No project arguments needed.
        --accept-current-files  Some files the patcher manages were changed by something
                                else (usually a game update). Treat their current contents
                                as the new originals, then apply.
        -f, --force             Apply patches even when a field's current value does not
                                match the patch's expected baseline. Use when the game has
                                been updated since the patch was created.
        -v, --verbose           Print each conversation as it is patched.
        -q, --quiet             Suppress all output except errors.
        --dry-run               Validate and plan the apply without writing any files.
        --version               Print version and the file formats it reads, and exit.
        -h, --help              Show this help.

      Exit codes:
        0   All patches applied (or restore / dry-run completed) successfully.
        1   Patch conflict detected. Re-run with --force to override.
        2   Argument, file, game-detection or backup-record error.
        3   Files the patcher manages were changed outside it. Re-run with
            --accept-current-files, or with --restore.
        4   A project or pack uses a newer file format than this patcher reads.
            Nothing was changed. Update dialog-patcher: {SchemaFormats.PatcherReleasesUrl}
      """;

    private const string DoubleClickNote = """
      This is dialog-patcher, the command-line tool for scripts and mod installers.

      To install or remove mods, open DialogEditor.PatchManager.exe in the folder above
      this one instead.

      """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, IConsoleLaunch? launch = null)
    {
        // Double-clicked with nothing to do: explain and pause, so the window doesn't flash
        // help text and vanish like a crash (issue #77). Terminal and script runs fall
        // through to the usual missing-argument error and exit code.
        if (args.Length == 0 && launch is { OwnsConsole: true })
        {
            stdout.WriteLine(DoubleClickNote);
            stdout.WriteLine(Help);
            stdout.WriteLine("Press any key to close this window.");
            launch.WaitForKey();
            return 0;
        }

        var version = AppVersion.FromAssembly(Assembly.GetExecutingAssembly());

        bool Has(params string[] flags) => flags.Any(args.Contains);

        bool force         = Has("-f", "--force");
        bool verbose       = Has("-v", "--verbose");
        bool quiet         = Has("-q", "--quiet");
        bool dryRun        = Has("--dry-run");
        bool restore       = Has("--restore");
        bool acceptCurrent = Has("--accept-current-files");

        void Info(string msg)    { if (!quiet) stdout.WriteLine(msg); }
        void Verbose(string msg) { if (verbose && !quiet) stdout.WriteLine(msg); }
        void Error(string msg)   => stderr.WriteLine($"Error: {msg}");

        int CorruptManifest(PatcherBackupCorruptException ex)
        {
            AppLog.Error("dialog-patcher: backup manifest unreadable", ex);
            Error($"The patcher's backup record is unreadable: {ex.ManifestPath}");
            stderr.WriteLine("Nothing was changed. Use your storefront's 'verify game files' to get clean originals,");
            stderr.WriteLine("then delete the PillarsDialogPatcher folder in the game directory.");
            return 2;
        }

        if (Has("-h", "--help")) { stdout.WriteLine(Help); return 0; }
        if (Has("--version"))
        {
            stdout.WriteLine($"dialog-patcher {version}");
            stdout.WriteLine($"reads {SchemaFormats.EnglishSummary}");
            return 0;
        }

        // ── Parse arguments ───────────────────────────────────────────────

        var positional = args.Where(a => !a.StartsWith('-')).ToArray();
        var required   = restore ? 1 : 2;

        if (positional.Length < required)
        {
            Error(restore
                ? "Missing required argument: <game-dir>"
                : "Missing required arguments: <game-dir> <project.dialogproject>");
            stderr.WriteLine();
            stderr.WriteLine(Help);
            return 2;
        }

        var gameDir      = positional[0];
        var projectPaths = positional[1..];

        // ── Validate paths ────────────────────────────────────────────────

        if (!Directory.Exists(gameDir))
        {
            Error($"Game directory not found: {gameDir}");
            return 2;
        }

        foreach (var path in projectPaths)
        {
            if (!File.Exists(path))
            {
                Error($"Project file not found: {path}");
                return 2;
            }
        }

        // ── Detect game ───────────────────────────────────────────────────

        var provider = GameDataProviderFactory.Detect(gameDir);
        if (provider is null)
        {
            Error($"Could not detect a Pillars of Eternity installation at: {gameDir}");
            Error("Expected a PoE1 or PoE2 game root directory.");
            return 2;
        }

        Info($"Game:    {provider.GameName}");

        // ── Restore ───────────────────────────────────────────────────────

        if (restore)
        {
            try
            {
                var r = PatchInstaller.Restore(gameDir);
                Info($"Restored {r.Restored} file(s).");
                foreach (var p in r.Skipped)
                    stderr.WriteLine($"Warning: left as is (changed outside the patcher, probably by a game update): {p}");
                return 0;
            }
            catch (PatcherBackupCorruptException ex) { return CorruptManifest(ex); }
            catch (Exception ex)
            {
                AppLog.Error("dialog-patcher: restore failed", ex);
                Error($"Restore failed: {ex.Message}");
                return 2;
            }
        }

        // ── Load projects ─────────────────────────────────────────────────

        var tempDirs = new List<string>();
        var entries  = new List<InstallEntry>();

        foreach (var path in projectPaths)
        {
            try
            {
                string effectivePath = path;
                string? voFolder     = null;

                if (DialogPackHelper.IsDialogPack(path))
                {
                    var extracted = DialogPackHelper.Extract(path);
                    effectivePath = extracted.ProjectFilePath;
                    voFolder      = extracted.VoFolderPath;
                    tempDirs.Add(extracted.TempDir);
                }

                var p = DialogProjectSerializer.LoadFromFile(effectivePath);
                entries.Add(new InstallEntry(p, voFolder));
                Info($"Project: {p.Name}  ({p.Patches.Count} patch(es))  [{path}]");
            }
            catch (UnsupportedSchemaVersionException ex)
            {
                // Refused in the load phase, before PatchInstaller, so nothing is written (GitHub issue 62).
                AppLog.Warn($"dialog-patcher: '{path}' needs a newer patcher: {ex.Message}");
                Error($"Could not load '{path}': {ex.Message}");
                stderr.WriteLine($"Nothing was changed. Get the latest dialog-patcher: {SchemaFormats.PatcherReleasesUrl}");
                CleanupTempDirs(tempDirs);
                return 4;
            }
            catch (Exception ex)
            {
                AppLog.Error($"dialog-patcher: could not load '{path}'", ex);
                Error($"Could not load '{path}': {ex.Message}");
                CleanupTempDirs(tempDirs);
                return 2;
            }
        }

        // ── Warn about cross-mod conflicts ────────────────────────────────
        // The merge resolves every overlap last-wins, so say which ones exist before it
        // silently does (issue #6). A warning, not an error: overriding an earlier mod is often
        // the point of load order, and the exit codes stay as documented.

        var crossModConflicts = ConflictDetector.Detect(entries
            .Select(e => (e.Project.Name, (IReadOnlyDictionary<string, ConversationPatch>)e.Project.Patches))
            .ToList());

        if (crossModConflicts.Count > 0 && !quiet)
        {
            var names = entries.Select(e => e.Project.Name).ToList();
            stderr.WriteLine(
                $"Warning: {crossModConflicts.Count} cross-mod conflict(s); projects later on the command line win:");
            foreach (var c in crossModConflicts)
                stderr.WriteLine($"  {CrossModConflictReport.Describe(c, names)}");
        }

        // ── Dry run ───────────────────────────────────────────────────────

        if (dryRun)
        {
            try
            {
                var plan = PatchInstaller.Plan(gameDir, entries);
                Info($"Dry run: would restore {plan.FilesToRestoreFirst} file(s) first, " +
                     $"then patch {plan.ConversationsToPatch} conversation(s).");
                if (plan.ExternallyChanged.Count > 0)
                {
                    stderr.WriteLine("Warning: these files were changed outside the patcher; a real run would stop");
                    stderr.WriteLine("and ask for --accept-current-files:");
                    foreach (var p in plan.ExternallyChanged) stderr.WriteLine($"  {p}");
                }
                return 0;
            }
            catch (PatcherBackupCorruptException ex) { return CorruptManifest(ex); }
            finally { CleanupTempDirs(tempDirs); }
        }

        // ── Apply ─────────────────────────────────────────────────────────

        InstallResult result;
        try
        {
            result = PatchInstaller.Install(provider, gameDir, entries,
                new InstallOptions(Force: force, AcceptCurrentFiles: acceptCurrent));
        }
        catch (PatcherBackupCorruptException ex)
        {
            CleanupTempDirs(tempDirs);
            return CorruptManifest(ex);
        }
        catch (PatchConflictException ex)
        {
            CleanupTempDirs(tempDirs);
            AppLog.Error("dialog-patcher: patch conflict", ex);
            stderr.WriteLine();
            stderr.WriteLine("Patch conflict detected:");
            stderr.WriteLine($"  Node ID:  {ex.NodeId}");
            stderr.WriteLine($"  Field:    {ex.FieldName}");
            stderr.WriteLine($"  Expected: {ex.ExpectedFrom}");
            stderr.WriteLine($"  Found:    {ex.ActualValue}");
            stderr.WriteLine();
            stderr.WriteLine("The game file has changed since this patch was created.");
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
                foreach (var p in a.UnreadableStringTables)
                    stderr.WriteLine($"Warning: text file is damaged (not XML) and was left untouched; that language shows no new text. Use \"verify files\" to repair it: {p}");
                Verbose($"  restored originals, then applied {entries.Count} project(s)");
                Info(a.MissingConversations.Count > 0
                    ? $"Done: {a.ConversationsPatched} patched, {a.MissingConversations.Count} skipped (conversation not found)."
                    : $"Done: {a.ConversationsPatched} conversation(s) patched successfully.");
                if (a.VoFilesCopied > 0) Info($"Copied {a.VoFilesCopied} VO file(s).");
                return 0;

            default:
                return 2;
        }
    }

    private static void CleanupTempDirs(IEnumerable<string> dirs)
    {
        foreach (var d in dirs)
        {
            try { Directory.Delete(d, recursive: true); }
            catch (Exception ex) { AppLog.Warn($"Failed to clean up temp dir '{d}': {ex.Message}"); }
        }
    }
}
