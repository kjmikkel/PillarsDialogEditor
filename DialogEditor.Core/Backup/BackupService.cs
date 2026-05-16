namespace DialogEditor.Core.Backup;

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

    public static async Task RestoreAsync(
        string backupRoot,
        string destRoot,
        CancellationToken ct,
        IProgress<string>? progress = null)
    {
        await BackupAsync(backupRoot, destRoot, ct, progress);
    }
}
