namespace DialogEditor.Core.Backup;

/// Takes the one-time snapshot of a game folder's conversations and stringtables. Restoring
/// it is <see cref="FullBackupRestore"/>'s job: a blind copy back could downgrade files a game
/// update changed since (issue 118).
public static class BackupService
{
    public static async Task BackupAsync(
        string sourceRoot,
        string destRoot,
        CancellationToken ct,
        IProgress<string>? progress = null)
    {
        Directory.CreateDirectory(destRoot);
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, file);
            var target   = Path.Combine(destRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            progress?.Report(relative);
        }
        await Task.CompletedTask;
    }
}
