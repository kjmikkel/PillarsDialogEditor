using System.Text.RegularExpressions;
using DialogEditor.ViewModels;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// Ties <see cref="GuidedTourViewModel.DefaultSteps"/> to the string catalogue that
/// actually backs it at runtime.
///
/// Why this guard exists: the tour's step copy lives in Strings.axaml and is reached
/// only through Loc.Get(step.DescriptionKey) — a late, string-keyed lookup. Nothing in
/// the compiler or the rest of the suite connects the two, so when the Docking Shell
/// Phase 1 cleanup removed the Tour_Step1_Text / Tour_Step3_Text entries, the build
/// stayed green and the break would only have surfaced as "[Tour_Step1_Text]" in front
/// of a first-run user. This test makes a missing key a build failure instead.
/// </summary>
public class GuidedTourStringCoverageTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // <sys:String x:Key="Tour_Step1_Text">The conversation browser…</sys:String>
    private static readonly Regex Entry = new(
        @"<sys:String\s+x:Key=""(?<key>[^""]+)"">(?<value>.*?)</sys:String>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static Dictionary<string, string> Catalogue()
    {
        var path = Path.Combine(SolutionRoot(), "DialogEditor.Avalonia", "Resources", "Strings.axaml");
        Assert.True(File.Exists(path), $"String catalogue not found at {path}");

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Entry.Matches(File.ReadAllText(path)))
            map[m.Groups["key"].Value] = m.Groups["value"].Value;
        return map;
    }

    [Fact]
    public void EveryDefaultTourStepHasCopyInTheStringCatalogue()
    {
        var catalogue = Catalogue();

        var missing = GuidedTourViewModel.DefaultSteps
            .Select(s => s.DescriptionKey)
            .Where(key => !catalogue.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
            .ToList();

        Assert.True(missing.Count == 0,
            "Guided tour steps reference string keys that are missing (or empty) in " +
            $"Strings.axaml: {string.Join(", ", missing)}. Every step's DescriptionKey must " +
            "have copy, or the tour shows a raw key to a first-run user.");
    }
}
