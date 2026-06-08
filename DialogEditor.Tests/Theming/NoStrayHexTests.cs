using System.Text.RegularExpressions;
using Xunit;

namespace DialogEditor.Tests.Theming;

public class NoStrayHexTests
{
    // Repo root: walk up from the test bin dir until we find the Avalonia project folder.
    private static string AvaloniaRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "DialogEditor.Avalonia")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "DialogEditor.Avalonia");
    }

    private static readonly Regex Hex = new(@"#[0-9A-Fa-f]{3,8}\b", RegexOptions.Compiled);
    private static readonly Regex CSharpColour = new(
        @"new\s+SolidColorBrush|Color\.FromRgb|Color\.FromArgb|Color\.Parse", RegexOptions.Compiled);

    [Fact]
    public void NoHexLiteralsOutsidePalette()
    {
        var root = AvaloniaRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.EndsWith("Palette.axaml", StringComparison.OrdinalIgnoreCase)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (Hex.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(offenders.Count == 0,
            "Hex colour literals are only allowed in Palette.axaml. Offenders:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void NoBrushConstructionInConverters()
    {
        var dir = Path.Combine(AvaloniaRoot(), "Converters");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (CSharpColour.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(offenders.Count == 0,
            "Converters must resolve tokens, not construct colours. Offenders:\n" + string.Join("\n", offenders));
    }
}
