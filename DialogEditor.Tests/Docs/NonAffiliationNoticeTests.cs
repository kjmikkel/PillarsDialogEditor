using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DialogEditor.Tests.Docs;

/// <summary>
/// The editor and the patcher are unofficial fan tools that name Obsidian's games
/// throughout. Every place a player or modder meets them must say so, name the
/// trademarks, and say that no game files are included (issue #64).
/// </summary>
public class NonAffiliationNoticeTests
{
    // The phrases that make a notice complete. Wording around them may vary with the
    // format (Markdown, BBCode, a localised string); these three facts may not.
    private static readonly string[] RequiredPhrases =
    [
        "not made, endorsed or supported by Obsidian Entertainment",
        "trademarks of their respective owners",
        "contains no game files",
    ];

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([SolutionRoot(), .. parts]));

    private static void AssertComplete(string text, string where)
    {
        // Markdown wraps prose anywhere, and quoted lines start with "> ", so compare the
        // text as it reads, not as it is laid out in the file.
        var flat = Regex.Replace(Regex.Replace(text, @"\r?\n>?[ \t]*", " "), @"\s+", " ");
        var missing = RequiredPhrases.Where(p => !flat.Contains(p, StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, $"{where} is missing: " + string.Join(" | ", missing));
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/patcher/README.md")]
    [InlineData("docs/nexus/patcher.md")]
    [InlineData(".github/release-notice.md")]
    public void Document_CarriesTheNotice(string relativePath) =>
        AssertComplete(Read(relativePath.Split('/')), relativePath);

    [Fact]
    public void Readme_ShowsTheNoticeBeforeTheFirstSection()
    {
        // "Near the top": a visitor sees it without scrolling past setup instructions.
        var readme = Read("README.md");
        var notice = readme.IndexOf("Obsidian Entertainment", StringComparison.Ordinal);
        var firstSection = readme.IndexOf("\n## ", StringComparison.Ordinal);

        Assert.True(notice >= 0 && notice < firstSection, "The README notice must come before the first ## section.");
    }

    [Fact]
    public void ReleaseWorkflow_PrependsTheNoticeToEveryRelease()
    {
        // gh prepends --notes-file text to the --generate-notes output.
        var workflow = Read(".github", "workflows", "release.yml");
        Assert.Contains("--notes-file .github/release-notice.md", workflow);
    }

    [Fact]
    public void AboutString_IsLocalisableAndComplete()
    {
        var dictionary = XDocument.Parse(Read("DialogEditor.Avalonia.Shared", "Resources", "SharedStrings.axaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var entry = dictionary.Descendants().SingleOrDefault(e => (string?)e.Attribute(x + "Key") == "About_NonAffiliation");

        Assert.NotNull(entry);
        AssertComplete(entry!.Value, "About_NonAffiliation");
    }

    [Theory]
    [InlineData("DialogEditor.Avalonia/Views/AboutWindow.axaml")]
    [InlineData("DialogEditor.PatchManager/PatchManagerAboutWindow.axaml")]
    public void AboutWindow_ShowsTheNotice(string relativePath) =>
        Assert.Contains("{DynamicResource About_NonAffiliation}", Read(relativePath.Split('/')));
}
