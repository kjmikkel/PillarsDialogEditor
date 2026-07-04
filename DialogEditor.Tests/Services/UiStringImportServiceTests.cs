using System.Text;
using System.Xml.Linq;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Services;

public class UiStringImportServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public UiStringImportServiceTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() { try { Directory.Delete(_tempDir, true); } catch { } }

    private string WriteCsv(string content)
    {
        var path = Path.Combine(_tempDir, "translation.csv");
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    private static XNamespace Sys => "clr-namespace:System;assembly=System.Runtime";

    private static IDictionary<string, string> ReadOverlay(string path)
    {
        var doc = XDocument.Load(path);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace sys = "clr-namespace:System;assembly=System.Runtime";
        return doc.Root!
            .Elements(sys + "String")
            .ToDictionary(e => e.Attribute(x + "Key")!.Value, e => e.Value);
    }

    // ── Basic import ──────────────────────────────────────────────────────

    [Fact]
    public void Import_ProducesOverlayFile_ForEachSourceFile()
    {
        var csv = WriteCsv("""
            Key,Source,Translation,File
            Greeting,Hello,Hallo,Strings.axaml
            """);
        UiStringImportService.Import(csv, "de", _tempDir);
        Assert.True(File.Exists(Path.Combine(_tempDir, "Strings.de.axaml")));
    }

    [Fact]
    public void Import_TranslatedValue_AppearsInOverlay()
    {
        var csv = WriteCsv("""
            Key,Source,Translation,File
            Greeting,Hello,Hallo,Strings.axaml
            """);
        UiStringImportService.Import(csv, "de", _tempDir);
        var overlay = ReadOverlay(Path.Combine(_tempDir, "Strings.de.axaml"));
        Assert.Equal("Hallo", overlay["Greeting"]);
    }

    [Fact]
    public void Import_EmptyTranslation_IsOmittedFromOverlay()
    {
        var csv = WriteCsv("""
            Key,Source,Translation,File
            Greeting,Hello,,Strings.axaml
            Farewell,Goodbye,Auf Wiedersehen,Strings.axaml
            """);
        UiStringImportService.Import(csv, "de", _tempDir);
        var overlay = ReadOverlay(Path.Combine(_tempDir, "Strings.de.axaml"));
        Assert.False(overlay.ContainsKey("Greeting"));
        Assert.True(overlay.ContainsKey("Farewell"));
    }

    // ── Multiple source files ─────────────────────────────────────────────

    [Fact]
    public void Import_MultipleFiles_ProducesSeparateOverlays()
    {
        var csv = WriteCsv("""
            Key,Source,Translation,File
            A,Alpha,Alfa,Strings.axaml
            B,Beta,Beta,SharedStrings.axaml
            """);
        UiStringImportService.Import(csv, "de", _tempDir);
        Assert.True(File.Exists(Path.Combine(_tempDir, "Strings.de.axaml")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "SharedStrings.de.axaml")));
    }

    [Fact]
    public void Import_EachKeyGoesIntoItsSourceFileOverlay()
    {
        var csv = WriteCsv("""
            Key,Source,Translation,File
            A,Alpha,Alfa,Strings.axaml
            B,Beta,Beta,SharedStrings.axaml
            """);
        UiStringImportService.Import(csv, "de", _tempDir);
        var main   = ReadOverlay(Path.Combine(_tempDir, "Strings.de.axaml"));
        var shared = ReadOverlay(Path.Combine(_tempDir, "SharedStrings.de.axaml"));
        Assert.True(main.ContainsKey("A"));
        Assert.False(main.ContainsKey("B"));
        Assert.True(shared.ContainsKey("B"));
        Assert.False(shared.ContainsKey("A"));
    }

    // ── Special characters ────────────────────────────────────────────────

    [Fact]
    public void Import_AmpersandInTranslation_IsRoundTrippedCorrectly()
    {
        // The overlay AXAML must encode & as &amp; — XDocument handles this automatically
        var csv = WriteCsv("""
            Key,Source,Translation,File
            "K","A & B","C & D",Strings.axaml
            """);
        UiStringImportService.Import(csv, "de", _tempDir);
        var overlay = ReadOverlay(Path.Combine(_tempDir, "Strings.de.axaml"));
        Assert.Equal("C & D", overlay["K"]);
    }

    [Fact]
    public void Import_QuotedCsvValue_IsUnquotedCorrectly()
    {
        var csv = WriteCsv("""
            Key,Source,Translation,File
            K,Hello,"Hallo, Welt",Strings.axaml
            """);
        UiStringImportService.Import(csv, "de", _tempDir);
        var overlay = ReadOverlay(Path.Combine(_tempDir, "Strings.de.axaml"));
        Assert.Equal("Hallo, Welt", overlay["K"]);
    }

    // ── Overlay file structure ────────────────────────────────────────────

    [Fact]
    public void Import_OverlayFile_IsValidXml()
    {
        var csv = WriteCsv("""
            Key,Source,Translation,File
            K,V,T,Strings.axaml
            """);
        UiStringImportService.Import(csv, "de", _tempDir);
        // Should not throw
        var doc = XDocument.Load(Path.Combine(_tempDir, "Strings.de.axaml"));
        Assert.NotNull(doc.Root);
    }

    [Fact]
    public void Import_OverlayFile_RootIsResourceDictionary()
    {
        var csv = WriteCsv("""
            Key,Source,Translation,File
            K,V,T,Strings.axaml
            """);
        UiStringImportService.Import(csv, "de", _tempDir);
        var doc = XDocument.Load(Path.Combine(_tempDir, "Strings.de.axaml"));
        Assert.Equal("ResourceDictionary", doc.Root!.Name.LocalName);
    }

    // ── Language overlay marker ───────────────────────────────────────────

    [Fact]
    public void Import_OverlayFile_ContainsLanguageOverlayMarkerWithLanguageCode()
    {
        // The _LanguageOverlayMarker sentinel is required by LanguageApplier to locate
        // and remove the overlay when the user switches language.
        var csv = WriteCsv("""
            Key,Source,Translation,File
            K,V,T,Strings.axaml
            """);
        UiStringImportService.Import(csv, "de", _tempDir);
        var overlay = ReadOverlay(Path.Combine(_tempDir, "Strings.de.axaml"));
        Assert.True(overlay.ContainsKey("_LanguageOverlayMarker"),
            "Overlay must contain _LanguageOverlayMarker so LanguageApplier can remove it on language switch.");
        Assert.Equal("de", overlay["_LanguageOverlayMarker"]);
    }

    [Fact]
    public void Import_LanguageOverlayMarker_IsFirstEntry()
    {
        // LanguageApplier iterates from the top; marker as first entry is the safest position.
        var csv = WriteCsv("""
            Key,Source,Translation,File
            K,V,T,Strings.axaml
            """);
        UiStringImportService.Import(csv, "fr", _tempDir);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var doc = XDocument.Load(Path.Combine(_tempDir, "Strings.fr.axaml"));
        var firstKey = doc.Root!
            .Elements(Sys + "String")
            .First()
            .Attribute(x + "Key")?.Value;
        Assert.Equal("_LanguageOverlayMarker", firstKey);
    }

    // ── OverlayFileName helper ────────────────────────────────────────────

    [Theory]
    [InlineData("Strings.axaml",       "de", "Strings.de.axaml")]
    [InlineData("SharedStrings.axaml", "fr", "SharedStrings.fr.axaml")]
    public void OverlayFileName_InsertsLanguageBeforeExtension(string fileId, string lang, string expected)
    {
        Assert.Equal(expected, UiStringImportService.OverlayFileName(fileId, lang));
    }

    [Fact]
    public void Import_AcceptsPluralCategoryRows_AbsentFromEnglishSource()
    {
        // A Polish translator adds _Few/_Many rows that English never ships
        // (see the 2026-07-04 pluralisation spec) — the importer must write
        // every translated row, not just keys present in the English export.
        var csv = WriteCsv("""
            Key,Source,Translation,File
            X_One,1 file,1 plik,Strings.axaml
            X_Few,,{0} pliki,Strings.axaml
            X_Many,,{0} plików,Strings.axaml
            """);
        UiStringImportService.Import(csv, "pl", _tempDir);
        var overlay = ReadOverlay(Path.Combine(_tempDir, "Strings.pl.axaml"));
        Assert.Equal("{0} pliki",  overlay["X_Few"]);
        Assert.Equal("{0} plików", overlay["X_Many"]);
    }
}
