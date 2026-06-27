using System.IO.Compression;
using DialogEditor.Avalonia.Audio;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Audio;

public class VoImporterTests
{
    [Theory]
    [InlineData(WemQuality.Low,    "VorbisLow")]
    [InlineData(WemQuality.Medium, "VorbisMedium")]
    [InlineData(WemQuality.High,   "VorbisHigh")]
    public void GenerateWsourcesXml_ContainsCorrectPresetName(
        WemQuality quality, string expectedPreset)
    {
        var xml = VoImporter.GenerateWsourcesXml(
            @"C:\sources\line_0001.wav",
            @"C:\dest\line_0001.wem",
            quality);

        Assert.Contains(expectedPreset, xml);
    }

    [Fact]
    public void GenerateWsourcesXml_ContainsSourceFileName()
    {
        var xml = VoImporter.GenerateWsourcesXml(
            @"C:\sources\line_0001.wav",
            @"C:\dest\line_0001.wem",
            WemQuality.Medium);

        Assert.Contains("line_0001.wav", xml);
    }

    [Fact]
    public void GenerateWsourcesXml_ContainsDestNameWithoutExtension()
    {
        var xml = VoImporter.GenerateWsourcesXml(
            @"C:\sources\line_0001.wav",
            @"C:\dest\line_0001.wem",
            WemQuality.Medium);

        Assert.Contains("line_0001", xml);
        Assert.DoesNotContain("line_0001.wem", xml); // destination is name-only, no extension
    }

    [Fact]
    public void GenerateWsourcesXml_UsesForwardSlashesInPaths()
    {
        var xml = VoImporter.GenerateWsourcesXml(
            @"C:\sources\sub\line_0001.wav",
            @"C:\dest\sub\line_0001.wem",
            WemQuality.Medium);

        Assert.DoesNotContain('\\', xml);
    }

    [Fact]
    public void ExtractTemplateZip_CreatesWprojFile()
    {
        using var ms = CreateStubZip();
        var destDir = Path.Combine(Path.GetTempPath(), $"VoImporterTest_{Guid.NewGuid():N}");
        try
        {
            var result = VoImporter.ExtractTemplateZip(ms, destDir);
            Assert.True(File.Exists(result));
        }
        finally
        {
            if (Directory.Exists(destDir))
                Directory.Delete(destDir, recursive: true);
        }
    }

    [Fact]
    public void ExtractTemplateZip_ReturnsPathEndingWithTemplateWproj()
    {
        using var ms = CreateStubZip();
        var destDir = Path.Combine(Path.GetTempPath(), $"VoImporterTest_{Guid.NewGuid():N}");
        try
        {
            var result = VoImporter.ExtractTemplateZip(ms, destDir);
            Assert.EndsWith("template.wproj", result, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(destDir))
                Directory.Delete(destDir, recursive: true);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private static MemoryStream CreateStubZip()
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "template/template.wproj",                   "<stub/>");
            WriteEntry(archive, "template/Conversion Settings/Factory.wwu",  "<stub/>");
        }
        ms.Position = 0;
        return ms;
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
