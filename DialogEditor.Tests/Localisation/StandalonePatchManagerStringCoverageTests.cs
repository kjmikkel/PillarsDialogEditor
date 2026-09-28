using System.Text.RegularExpressions;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// The standalone Patch Manager (Pillars Dialog Patcher, issue #77) loads only
/// SharedStrings.axaml and its own Strings.axaml — not the editor's big dictionary. A key
/// that lives only in the editor's Strings.axaml resolves fine inside the editor and shows
/// up as "[Key]" in the standalone app, which no editor-hosted test notices. That is how
/// the "Add mods…" file filter shipped labelled "[FileType_DialogProjectOrPack]" (issue #80).
///
/// So: every key the standalone app's code and views ask for must be defined in one of
/// the two dictionaries it merges.
/// </summary>
public class StandalonePatchManagerStringCoverageTests
{
    // Code the standalone exe runs that looks strings up through Loc.
    private static readonly string[] StandaloneSources =
    [
        "DialogEditor.ViewModels/ViewModels/PatchManagerViewModel.cs",
        "DialogEditor.ViewModels/ViewModels/PatchEntryViewModel.cs",
        "DialogEditor.ViewModels/ViewModels/PatchConflictRowViewModel.cs",
        "DialogEditor.ViewModels/ViewModels/PatchManagerAboutViewModel.cs",
        "DialogEditor.ViewModels/ViewModels/ThemePickerViewModel.cs",
        "DialogEditor.ViewModels/ViewModels/LanguagePickerViewModel.cs",
    ];

    // Dictionaries DialogEditor.PatchManager/App.axaml merges.
    private static readonly string[] StandaloneDictionaries =
    [
        "DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml",
        "DialogEditor.PatchManager/Resources/Strings.axaml",
    ];

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static HashSet<string> DefinedKeys(string root) =>
        StandaloneDictionaries
            .SelectMany(d => Regex.Matches(File.ReadAllText(Path.Combine(root, d)), "x:Key=\"([^\"]+)\""))
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    // Loc.FormatCount("X", n) resolves X_<plural category> and falls back to X_Other, so
    // X_Other is the key that must exist.
    private static IEnumerable<string> CodeKeys(string root) =>
        StandaloneSources.SelectMany(src =>
        {
            var text = File.ReadAllText(Path.Combine(root, src));
            return Regex.Matches(text, "Loc\\.(Get|Format|FormatCount)\\(\\s*\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value == "FormatCount" ? m.Groups[2].Value + "_Other" : m.Groups[2].Value);
        });

    // String keys in this codebase are Area_Name; design tokens (Brush.*, FontSize.*) use
    // dots and come from the theme dictionaries, which are out of scope here.
    private static IEnumerable<string> ViewKeys(string root) =>
        new[] { "DialogEditor.Avalonia.Shared", "DialogEditor.PatchManager" }
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(root, p), "*.axaml"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Resources{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), "(?:Dynamic|Static)Resource ([A-Za-z]+_[A-Za-z_]+)"))
            .Select(m => m.Groups[1].Value);

    [Fact]
    public void EveryKeyTheStandaloneAppUses_IsDefinedInADictionaryItLoads()
    {
        var root    = SolutionRoot();
        var defined = DefinedKeys(root);

        var missing = CodeKeys(root).Concat(ViewKeys(root))
            .Where(k => !defined.Contains(k))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "Keys the standalone Patch Manager would show as [Key] — move them to SharedStrings.axaml:\n  "
            + string.Join("\n  ", missing));
    }
}
