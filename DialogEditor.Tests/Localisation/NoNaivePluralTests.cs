using System.Text.RegularExpressions;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// Structural guard for the 2026-07-04 pluralisation work: the "(s)"/"(es)" idiom
/// cannot be translated into languages with more than two plural forms (Polish 4,
/// Arabic 6). Pluralised strings use _One/_Few/_Many/_Other… key pairs resolved by
/// Loc.FormatCount (see docs/superpowers/specs/2026-07-04-pluralisation-design.md);
/// this test keeps the old idiom from creeping back into the dictionaries.
/// </summary>
public class NoNaivePluralTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Theory]
    [InlineData("DialogEditor.Avalonia/Resources/Strings.axaml")]
    [InlineData("DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml")]
    public void StringValues_NeverUseNaivePluralSuffix(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(SolutionRoot(), relativePath));
        var offenders = Regex
            .Matches(text, @"x:Key=""([^""]+)""[^<]*\((?:e?s)\)")
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.Empty(offenders);
    }
}
