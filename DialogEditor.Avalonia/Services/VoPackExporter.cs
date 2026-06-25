using System.IO.Compression;

namespace DialogEditor.Avalonia.Services;

/// <summary>
/// Packages a .dialogproject and its _vo/ folder into a .dialogpack file (ZIP with custom extension).
/// Users can rename .dialogpack to .zip to inspect the contents with any archive tool.
/// </summary>
public static class VoPackExporter
{
    // Embedded verbatim copy of FORMAT.md — keeps content correct regardless of working directory.
    private const string FormatMdContent =
        """
        # .dialogpack format

        A `.dialogpack` file is a standard ZIP archive. Rename it to `.zip` to
        inspect or extract its contents with any archive tool.

        ## Contents

        - `project.dialogproject` — the dialog diff (JSON); apply with the
          Pillars Dialog Editor Patch Manager or the `dialog-patcher` CLI.
        - `vo/` — voice-over audio files in Wwise `.wem` format, laid out to
          mirror the game's VO directory structure. The Patch Manager and CLI
          copy these to the correct game folder location when applying the pack.
        - `FORMAT.md` — this file.

        ## Applying a .dialogpack

        **GUI:** Open the Pillars Dialog Editor Patch Manager, add the `.dialogpack`
        file, set your game folder, and click Apply.

        **CLI:**
        ```
        dialog-patcher <game-dir> mymod.dialogpack
        ```

        ## Voice-over directory structure

        `vo/` mirrors `PillarsOfEternityII_Data/StreamingAssets/Audio/Windows/Voices/English(US)/`:
        ```
        vo/
          eder/
            my_line_0001.wem
            my_line_0001_fem.wem
          narrator/
            my_line_0002.wem
        ```
        """;

    /// <summary>
    /// Returns true when a _vo/ folder exists next to the project file (i.e. export is meaningful).
    /// </summary>
    public static bool CanExport(string? projectPath)
    {
        if (string.IsNullOrEmpty(projectPath)) return false;
        var voFolder = Path.Combine(Path.GetDirectoryName(projectPath)!, "_vo");
        return Directory.Exists(voFolder);
    }

    /// <summary>
    /// Creates a .dialogpack at <paramref name="outputPath"/> containing:
    ///   project.dialogproject, vo/ (contents of _vo/), FORMAT.md.
    /// </summary>
    public static async Task ExportAsync(string projectPath, string outputPath,
        CancellationToken ct = default)
    {
        var projectDir = Path.GetDirectoryName(projectPath)!;
        var voFolder   = Path.Combine(projectDir, "_vo");

        if (File.Exists(outputPath)) File.Delete(outputPath);

        await Task.Run(() =>
        {
            using var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);

            // project.dialogproject → root of the archive as "project.dialogproject"
            zip.CreateEntryFromFile(projectPath, "project.dialogproject",
                CompressionLevel.Optimal);

            // _vo/ → vo/ inside the archive
            foreach (var file in Directory.EnumerateFiles(voFolder, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(voFolder, file).Replace('\\', '/');
                zip.CreateEntryFromFile(file, $"vo/{relative}", CompressionLevel.Optimal);
            }

            // FORMAT.md
            var formatEntry = zip.CreateEntry("FORMAT.md", CompressionLevel.Optimal);
            using var writer = new StreamWriter(formatEntry.Open());
            writer.Write(FormatMdContent);
        }, ct);
    }
}
