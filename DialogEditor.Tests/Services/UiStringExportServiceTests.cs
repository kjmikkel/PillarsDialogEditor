using System.Text;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Services;

public class UiStringExportServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public UiStringExportServiceTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() { try { Directory.Delete(_tempDir, true); } catch { } }

    private string WriteAxaml(string content)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid()}.axaml");
        File.WriteAllText(path, $"""
            <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:sys="clr-namespace:System;assembly=System.Runtime">
            {content}
            </ResourceDictionary>
            """, Encoding.UTF8);
        return path;
    }

    private string CsvPath() => Path.Combine(_tempDir, "export.csv");

    // ── Header ────────────────────────────────────────────────────────────

    [Fact]
    public void Export_CsvFirstLine_IsHeaderRow()
    {
        var axaml = WriteAxaml(@"<sys:String x:Key=""Foo"">Bar</sys:String>");
        UiStringExportService.Export([("Strings.axaml", axaml)], CsvPath());
        var lines = File.ReadAllLines(CsvPath());
        Assert.Equal("Key,Source,Translation,File", lines[0]);
    }

    // ── Row content ───────────────────────────────────────────────────────

    [Fact]
    public void Export_WritesKeyAndSourceText()
    {
        var axaml = WriteAxaml(@"<sys:String x:Key=""Hello_World"">Hello, world!</sys:String>");
        UiStringExportService.Export([("Strings.axaml", axaml)], CsvPath());
        var body = File.ReadAllLines(CsvPath()).Skip(1).First();
        Assert.Contains("Hello_World", body);
        Assert.Contains("Hello, world!", body);
    }

    [Fact]
    public void Export_WritesFileId()
    {
        var axaml = WriteAxaml(@"<sys:String x:Key=""K"">V</sys:String>");
        UiStringExportService.Export([("SharedStrings.axaml", axaml)], CsvPath());
        var body = File.ReadAllLines(CsvPath()).Skip(1).First();
        Assert.Contains("SharedStrings.axaml", body);
    }

    [Fact]
    public void Export_TranslationColumn_IsEmpty()
    {
        var axaml = WriteAxaml(@"<sys:String x:Key=""K"">V</sys:String>");
        UiStringExportService.Export([("Strings.axaml", axaml)], CsvPath());
        var fields = CsvSplit(File.ReadAllLines(CsvPath())[1]);
        Assert.Equal(string.Empty, fields[2]); // Translation column
    }

    [Fact]
    public void Export_XmlEntities_AreDecoded()
    {
        // &amp; in AXAML should appear as & in the CSV Source column
        var axaml = WriteAxaml(@"<sys:String x:Key=""K"">A &amp; B</sys:String>");
        UiStringExportService.Export([("Strings.axaml", axaml)], CsvPath());
        var fields = CsvSplit(File.ReadAllLines(CsvPath())[1]);
        Assert.Equal("A & B", fields[1]);
    }

    [Fact]
    public void Export_CommentsAndNonStringElements_AreSkipped()
    {
        var axaml = WriteAxaml("<!-- comment -->");
        UiStringExportService.Export([("Strings.axaml", axaml)], CsvPath());
        var lines = File.ReadAllLines(CsvPath());
        Assert.Single(lines); // only header
    }

    // ── Multiple source files ─────────────────────────────────────────────

    [Fact]
    public void Export_MultipleSourceFiles_AllKeysPresent()
    {
        var a = WriteAxaml(@"<sys:String x:Key=""A"">Alpha</sys:String>");
        var b = WriteAxaml(@"<sys:String x:Key=""B"">Beta</sys:String>");
        UiStringExportService.Export([("Strings.axaml", a), ("SharedStrings.axaml", b)], CsvPath());
        var content = File.ReadAllText(CsvPath());
        Assert.Contains("Alpha", content);
        Assert.Contains("Beta", content);
    }

    // ── CSV quoting ───────────────────────────────────────────────────────

    [Fact]
    public void Export_SourceWithComma_IsQuoted()
    {
        var axaml = WriteAxaml(@"<sys:String x:Key=""K"">Hello, world</sys:String>");
        UiStringExportService.Export([("Strings.axaml", axaml)], CsvPath());
        var line = File.ReadAllLines(CsvPath())[1];
        Assert.Contains("\"Hello, world\"", line);
    }

    [Fact]
    public void Export_SourceWithQuote_IsDoubledInCsv()
    {
        var axaml = WriteAxaml(@"<sys:String x:Key=""K"">Say ""hi""</sys:String>");
        UiStringExportService.Export([("Strings.axaml", axaml)], CsvPath());
        var line = File.ReadAllLines(CsvPath())[1];
        Assert.Contains("\"Say \"\"hi\"\"\"", line);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static List<string> CsvSplit(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuote = false;
        foreach (var c in line)
        {
            if (inQuote)
            {
                if (c == '"') inQuote = false;
                else current.Append(c);
            }
            else
            {
                if (c == '"') inQuote = true;
                else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return fields;
    }
}
