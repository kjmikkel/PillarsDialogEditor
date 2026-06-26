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
}
