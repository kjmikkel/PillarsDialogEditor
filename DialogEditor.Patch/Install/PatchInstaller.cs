using DialogEditor.Core.GameData;

namespace DialogEditor.Patch.Install;

/// <summary>
/// The one apply routine behind dialog-patcher and the Patch Manager (issue #76). Every
/// install first restores the originals from <see cref="PatcherBackupStore"/>, then applies
/// the whole load order, so the result depends only on the originals and the list — never on
/// what was applied before. Each file is backed up before its first write, and its written
/// state is recorded after, so "Remove all mods" can always undo exactly what was done.
/// </summary>
public static class PatchInstaller
{
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

        try
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

                // Back up every target before the first write, so a crash anywhere below
                // leaves a manifest that covers all of them.
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

            var voCopied = CopyVo(provider, gameDir, loadOrder, store, options);
            return new InstallResult.Applied(patched, missing, restoreSkipped, voCopied);
        }
        catch (PatchConflictException)
        {
            // Leave the game clean rather than half-modded; the caller reports the conflict.
            store.RestoreAll();
            throw;
        }
    }

    private static int CopyVo(
        IGameDataProvider provider, string gameDir, IReadOnlyList<InstallEntry> loadOrder,
        PatcherBackupStore store, InstallOptions options)
    {
        var voRoot = VoRoot(provider, gameDir);
        if (voRoot is null) return 0;

        var copied = 0;
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
                copied++;
            }
        }
        return copied;
    }

    /// PoE2 only: PoE1 keeps VO inside Unity asset archives the patcher cannot write (#4).
    /// The single home of this path for the patcher; the CLI and the Patch Manager used to
    /// hard-code it separately. Only English(US) until issue 101 adds the other VO languages.
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

    /// Dry run: what an install would do, without writing anything.
    public static InstallPlan Plan(string gameDir, IReadOnlyList<InstallEntry> loadOrder)
    {
        var store = PatcherBackupStore.Open(gameDir);
        return new InstallPlan(
            Merge(loadOrder).Patches.Count,
            store.Entries.Count,
            store.FindExternallyChanged());
    }

    /// The conversation and string-table serializers copy the previous file to "<path>.bak"
    /// before overwriting it, so each sidecar is a write target too — otherwise restore would
    /// leave stray .bak files in the game folder.
    private static List<string> WithSidecars(IEnumerable<string> paths) =>
        paths.SelectMany(p => new[] { p, p + ".bak" }).ToList();

    private static DialogProject Merge(IReadOnlyList<InstallEntry> loadOrder)
    {
        if (loadOrder.Count == 0) return DialogProject.Empty(string.Empty);
        var merged = loadOrder[0].Project;
        for (var i = 1; i < loadOrder.Count; i++)
            merged = merged.MergeWith(loadOrder[i].Project);
        return merged;
    }
}
