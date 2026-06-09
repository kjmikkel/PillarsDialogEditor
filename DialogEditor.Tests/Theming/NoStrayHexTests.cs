using System.Text.RegularExpressions;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// The colour-token contract enforcer (Layer 0 definition-of-done). Two tiers: hex
/// primitives live ONLY in the palette family (`Palette*.axaml`) (private tier); everything else — views,
/// control themes, converters, code-behind — binds the semantic Brush.* tokens in
/// Tokens.axaml (public tier) or resolves them via <c>TokenBrushes.Resolve</c>. These
/// tests fail the build if any hex literal escapes the palette family or any production type
/// constructs a brush, so "nothing constructs a colour any other way" is true rather
/// than aspirational.
///
/// Scope is the ENTIRE SOLUTION, not just DialogEditor.Avalonia: the shared
/// PatchManagerView (DialogEditor.Avalonia.Shared) and the standalone PatchManager app
/// host the same tokens, so the contract must hold app-wide — a hex literal in any
/// project is a violation. See
/// docs/superpowers/specs/2026-06-07-colour-token-taxonomy-design.md §11.
/// </summary>
public class NoStrayHexTests
{
    // Walk up from the test bin dir until we find the solution file, then scan every
    // project beneath it. Anchoring on the .slnx (not a single project folder) is what
    // makes the enforcer solution-wide.
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // Skip build output: Avalonia embeds .axaml rather than copying it, but bin/obj may
    // hold generated .g.cs whose compiled-XAML brush construction is not source we own.
    private static bool IsBuildArtifact(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    // The sanctioned hex tier is the whole palette family: Palette.Dark.axaml plus the Layer 1
    // alternates (Palette.Light/HighContrast/Colourblind.axaml). Any other filename with hex fails.
    private static readonly Regex PaletteFile =
        new(@"^Palette(\.[A-Za-z]+)?\.axaml$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Hex = new(@"#[0-9A-Fa-f]{3,8}\b", RegexOptions.Compiled);
    private static readonly Regex CSharpColour = new(
        @"new\s+SolidColorBrush|Color\.FromRgb|Color\.FromArgb|Color\.Parse", RegexOptions.Compiled);

    [Fact]
    public void NoHexLiteralsOutsidePalette()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(file)) continue;
            if (PaletteFile.IsMatch(Path.GetFileName(file))) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (Hex.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(offenders.Count == 0,
            "Hex colour literals are only allowed in the palette family (Palette*.axaml). Offenders:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void NoHexLiteralsInProductionCode()
    {
        // Closes the gap the brush-construction scan leaves open: a bare hex string
        // (e.g. var c = "#FF0000";) that is never passed to a brush/colour ctor would
        // otherwise slip through. Hex primitives belong only in the palette family, so no
        // production .cs may carry one. The Tests project legitimately names hex values
        // (asserting on the registry) and is excluded, as is the sanctioned resolver.
        var root = SolutionRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(file)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}DialogEditor.Tests{Path.DirectorySeparatorChar}")) continue;
            if (file.EndsWith("TokenBrushes.cs", StringComparison.OrdinalIgnoreCase)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (Hex.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(offenders.Count == 0,
            "Hex colour literals are only allowed in the palette family (Palette*.axaml), never in production code. Offenders:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void NoBrushConstructionInProductionCode()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(file)) continue;
            // The Tests project legitimately constructs colours to assert on the registry.
            if (file.Contains($"{Path.DirectorySeparatorChar}DialogEditor.Tests{Path.DirectorySeparatorChar}")) continue;
            // TokenBrushes is the single sanctioned colour-resolving seam (it resolves, never
            // constructs — but it is the one type allowed to touch brushes by name).
            if (file.EndsWith("TokenBrushes.cs", StringComparison.OrdinalIgnoreCase)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (CSharpColour.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(offenders.Count == 0,
            "Production code must resolve tokens (TokenBrushes.Resolve), not construct colours. Offenders:\n"
            + string.Join("\n", offenders));
    }
}
