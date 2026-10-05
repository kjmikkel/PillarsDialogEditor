using System.Text.RegularExpressions;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Localisation;

/// Issue 132: each Display Type / Persistence option reaches its label and tooltip only via
/// a key built at runtime ("Option_Display" + name), which nothing else checks. A missing
/// key would show "[Option_DisplayOverlay]" in the dropdown; this makes it a test failure.
public class NodeEnumOptionStringCoverageTests
{
    private static readonly Regex Entry = new(
        @"<sys:String\s+x:Key=""(?<key>[^""]+)"">(?<value>.*?)</sys:String>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static Dictionary<string, string> Catalogue()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "DialogEditor.Avalonia", "Resources", "Strings.axaml"));
        return Entry.Matches(text).ToDictionary(m => m.Groups["key"].Value, m => m.Groups["value"].Value);
    }

    public static TheoryData<string> RequiredKeys()
    {
        var data = new TheoryData<string>();
        foreach (var name in NodeEnumNames.DisplayTypes.Append("NotSet"))
        {
            data.Add($"Option_Display{name}");
            data.Add($"ToolTip_Option_Display{name}");
        }
        foreach (var name in NodeEnumNames.Persistences.Append("NotSet"))
        {
            data.Add($"Option_Persistence{name}");
            data.Add($"ToolTip_Option_Persistence{name}");
        }
        data.Add("ToolTip_Option_Display_Fallback");
        data.Add("ToolTip_Option_Persistence_Fallback");
        return data;
    }

    [Theory]
    [MemberData(nameof(RequiredKeys))]
    public void OptionKeyHasCopy(string key) =>
        Assert.True(Catalogue().TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text),
            $"Strings.axaml has no copy for '{key}'.");
}
