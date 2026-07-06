using System.Text.RegularExpressions;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

/// The insert templates drive autocomplete's "caret at first placeholder"
/// behaviour. A parameterised token ([Specified n]) or paired/attribute markup
/// (<i>…</i>, <color="…">…</color>) MUST carry an `insert`; plain tokens
/// ([Player Name]) may omit it (they insert Name verbatim).
/// Spec: docs/superpowers/specs/2026-07-06-token-autocomplete-design.md
public class TagInsertTemplateTests
{
    private static readonly Regex Marker = new(@"\$\{[^}]*\}", RegexOptions.Compiled);

    private static bool NeedsInsert(TagEntry e) =>
        (e.Kind == "Token" || e.Kind == "Markup") &&
        (e.Name.Contains('…') || e.Name.EndsWith(" n]"));

    [Fact]
    public void ParameterisedAndPairedEntries_HaveInsert()
    {
        foreach (var e in TagCatalogue.Instance.All)
            if (NeedsInsert(e))
                Assert.False(string.IsNullOrEmpty(e.Insert),
                    $"'{e.Name}' needs an insert template.");
    }

    [Fact]
    public void EveryInsert_HasExactlyOneMarker()
    {
        foreach (var e in TagCatalogue.Instance.All)
            if (!string.IsNullOrEmpty(e.Insert))
                Assert.Single(Marker.Matches(e.Insert!));
    }

    [Fact]
    public void InsertPrefix_AgreesWithName()
    {
        // The part of `insert` before its marker must be a prefix of `Name`,
        // guarding typos between the display and insert forms.
        foreach (var e in TagCatalogue.Instance.All)
        {
            if (string.IsNullOrEmpty(e.Insert)) continue;
            var open = e.Insert!.IndexOf("${", System.StringComparison.Ordinal);
            var prefix = e.Insert[..open];
            Assert.StartsWith(prefix, e.Name, System.StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KnownSample_Specified_HasExpectedInsert()
    {
        var specified = TagCatalogue.Instance.All.Single(e => e.Name == "[Specified n]");
        Assert.Equal("[Specified ${0}]", specified.Insert);
    }
}
