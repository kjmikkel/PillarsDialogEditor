using System.IO.Compression;
using DialogEditor.Avalonia.Services;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

/// A .dialogpack mirrors the project's reality: vo/ is present exactly when the
/// project has a _vo/ folder. A text-only project must export a valid VO-less
/// pack (the gate that used to block this lives in MainWindowViewModel).
/// Spec: docs/superpowers/specs/2026-07-05-export-without-vo-design.md
public class VoPackExporterTests : IDisposable
{
    private readonly string _tempDir;

    public VoPackExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"vopack_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private string WriteProject()
    {
        var path = Path.Combine(_tempDir, "mod.dialogproject");
        DialogProjectSerializer.SaveToFile(path, DialogProject.Empty("mod"));
        return path;
    }

    [Fact]
    public async Task Export_WithVoFolder_IncludesVoEntries()
    {
        var projectPath = WriteProject();
        var voDir = Path.Combine(_tempDir, "_vo", "eder");
        Directory.CreateDirectory(voDir);
        File.WriteAllBytes(Path.Combine(voDir, "line_0001.wem"), [1, 2, 3]);
        var output = Path.Combine(_tempDir, "out.dialogpack");

        await VoPackExporter.ExportAsync(projectPath, output);

        var result = DialogPackHelper.Extract(output);
        try
        {
            Assert.NotNull(result.VoFolderPath);
            Assert.True(File.Exists(Path.Combine(result.VoFolderPath!, "eder", "line_0001.wem")));
        }
        finally { Directory.Delete(result.TempDir, recursive: true); }
    }

    [Fact]
    public async Task Export_WithoutVoFolder_ProducesValidVoLessPack()
    {
        var projectPath = WriteProject();
        var output = Path.Combine(_tempDir, "out.dialogpack");

        await VoPackExporter.ExportAsync(projectPath, output);

        using (var zip = ZipFile.OpenRead(output))
        {
            Assert.Contains(zip.Entries, e => e.FullName == "project.dialogproject");
            Assert.Contains(zip.Entries, e => e.FullName == "FORMAT.md");
            Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("vo/"));
        }

        var result = DialogPackHelper.Extract(output);
        try { Assert.Null(result.VoFolderPath); }
        finally { Directory.Delete(result.TempDir, recursive: true); }
    }
}
