using System.IO.Compression;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Services;

/// <summary>
/// Packages a .dialogproject — and its _vo/ folder, when one exists — into a
/// .dialogpack file (ZIP with custom extension).
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
          mirror the game's VO directory structure. Present only when the mod
          contains voice-over; the Patch Manager and CLI copy these to the
          correct game folder location when applying the pack.
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
            try
            {
                using var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);

                // project.dialogproject → root of the archive as "project.dialogproject"
                zip.CreateEntryFromFile(projectPath, "project.dialogproject",
                    CompressionLevel.Optimal);

                // _vo/ → vo/ inside the archive — present exactly when the project
                // has voice-over; a text-only project exports a valid VO-less pack.
                if (Directory.Exists(voFolder))
                {
                    foreach (var file in Directory.EnumerateFiles(voFolder, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(voFolder, file).Replace('\\', '/');
                        zip.CreateEntryFromFile(file, $"vo/{relative}", CompressionLevel.Optimal);
                    }
                }

                // FORMAT.md
                var formatEntry = zip.CreateEntry("FORMAT.md", CompressionLevel.Optimal);
                using var writer = new StreamWriter(formatEntry.Open());
                writer.Write(FormatMdContent);
            }
            catch
            {
                // Delete the partial output so the caller doesn't see a corrupt archive.
                // The delete is best-effort: the original exception (rethrown below) is
                // what the caller must see, so a cleanup failure is only worth a warning.
                try { if (File.Exists(outputPath)) File.Delete(outputPath); }
                catch (Exception cleanupEx) { AppLog.Warn($"VoPackExporter: could not delete partial output '{outputPath}': {cleanupEx.Message}"); }
                throw;
            }
        }, ct);
    }
}
