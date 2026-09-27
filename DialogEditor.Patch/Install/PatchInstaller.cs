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

    private static DialogProject Merge(IReadOnlyList<InstallEntry> loadOrder)
    {
        if (loadOrder.Count == 0) return DialogProject.Empty(string.Empty);
        var merged = loadOrder[0].Project;
        for (var i = 1; i < loadOrder.Count; i++)
            merged = merged.MergeWith(loadOrder[i].Project);
        return merged;
    }
}
