using System.IO.Compression;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Extracts a .dialogpack (ZIP with custom extension) to a temp directory.
/// A .dialogpack must contain "project.dialogproject" at the root.
/// It may also contain a "vo/" folder with .wem audio files.
/// Returns the path to the extracted project.dialogproject and the vo/ folder (if present).
/// The caller is responsible for deleting TempDir when it is no longer needed.
/// </summary>
public static class DialogPackHelper
{
    public record ExtractResult(string ProjectFilePath, string? VoFolderPath, string TempDir);

    /// <summary>
    /// Extracts the .dialogpack to a uniquely-named temp directory.
    /// Caller must delete <see cref="ExtractResult.TempDir"/> when done.
    /// </summary>
    /// <param name="dialogPackPath">Full path to the .dialogpack file.</param>
    /// <returns>Paths to the extracted project file, optional vo/ folder, and the temp directory.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the archive does not contain project.dialogproject.</exception>
    public static ExtractResult Extract(string dialogPackPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PillarsDialogEditor",
            "dialogpack", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        ZipFile.ExtractToDirectory(dialogPackPath, tempDir, overwriteFiles: true);

        var projectFile = Path.Combine(tempDir, "project.dialogproject");
        if (!File.Exists(projectFile))
            throw new InvalidOperationException(
                $"Invalid .dialogpack: 'project.dialogproject' not found inside '{Path.GetFileName(dialogPackPath)}'.");

        var voFolder = Path.Combine(tempDir, "vo");
        return new ExtractResult(projectFile, Directory.Exists(voFolder) ? voFolder : null, tempDir);
    }

    /// <summary>
    /// Copies all .wem files from <paramref name="voFolder"/> to <paramref name="gameVoRoot"/>,
    /// preserving the relative directory structure.
    /// </summary>
    /// <param name="voFolder">Source vo/ directory extracted from the .dialogpack.</param>
    /// <param name="gameVoRoot">Destination VO root inside the game installation.</param>
    public static void CopyVoToGame(string voFolder, string gameVoRoot)
    {
        foreach (var file in Directory.EnumerateFiles(voFolder, "*.wem", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(voFolder, file);
            var dest     = Path.Combine(gameVoRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    /// <summary>Returns true if the path has a .dialogpack extension (case-insensitive).</summary>
    public static bool IsDialogPack(string path) =>
        path.EndsWith(".dialogpack", StringComparison.OrdinalIgnoreCase);
}
