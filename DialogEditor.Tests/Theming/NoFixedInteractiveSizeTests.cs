using System.Xml;
using System.Xml.Linq;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Guard for issue 177: interactive controls never get a fixed Width or Height. Their text
/// follows the font scale (issue 176), so a fixed box clips it at large scales. Use
/// MinWidth/MinHeight instead.
/// </summary>
public class NoFixedInteractiveSizeTests
{
    private static readonly HashSet<string> Interactive =
        ["Button", "ToggleButton", "ComboBox", "TextBox", "AutoCompleteBox", "CheckBox", "RadioButton", "NumericUpDown"];

    private static readonly string[] Projects =
        ["DialogEditor.Avalonia", "DialogEditor.Avalonia.Shared", "DialogEditor.PatchManager"];

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void InteractiveControls_HaveNoFixedWidthOrHeight()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();
        foreach (var project in Projects)
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, project), "*.axaml", SearchOption.AllDirectories))
        {
            var segments = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Contains("bin") || segments.Contains("obj")) continue;

            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var el in doc.Descendants().Where(e => Interactive.Contains(e.Name.LocalName)))
            foreach (var attr in new[] { "Width", "Height" })
                if (el.Attribute(attr) is { } a)
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{((IXmlLineInfo)el).LineNumber}: <{el.Name.LocalName}> {attr}=\"{a.Value}\"");
        }

        Assert.True(offenders.Count == 0,
            "Interactive controls must use MinWidth/MinHeight, not a fixed Width/Height, so their text can follow the font scale (issue 177):\n"
            + string.Join("\n", offenders));
    }
}
