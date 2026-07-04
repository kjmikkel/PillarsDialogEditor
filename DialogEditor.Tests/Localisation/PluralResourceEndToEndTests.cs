using System.Globalization;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Localisation;

// End-to-end: the real Strings.axaml/SharedStrings.axaml dictionaries resolved
// through the real AvaloniaStringProvider. The stub-based FormatCount tests
// prove the fallback mechanism; these pin that the shipped English resources
// actually contain the _One/_Other keys FormatCount computes at runtime — a
// key typo in the dictionaries passes every stub test and only surfaces here
// (or in the running app). Covers the manual-verification checklist of
// docs/superpowers/plans/2026-07-04-pluralisation.md.
public class PluralResourceEndToEndTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentUICulture;

    public PluralResourceEndToEndTests()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        Loc.Configure(new AvaloniaStringProvider());
    }

    public void Dispose() => CultureInfo.CurrentUICulture = _originalCulture;

    [AvaloniaTheory]
    [InlineData(1, "1 match")]
    [InlineData(3, "3 matches")]
    public void FindReplace_MatchCount_UsesRealResources(int count, string expected)
        => Assert.Equal(expected, Loc.FormatCount("FindReplace_Matches", count));

    [AvaloniaTheory]
    [InlineData(2, 1, "2 matches across 1 conversation")]
    [InlineData(1, 5, "1 match across 5 conversations")]
    public void BatchReplace_ComposedStatus_UsesRealResources(
        int matches, int conversations, string expected)
        => Assert.Equal(expected, Loc.Format("BatchReplace_StatusMatches",
            Loc.FormatCount("BatchReplace_MatchCount", matches),
            Loc.FormatCount("BatchReplace_ConversationCount", conversations)));

    [AvaloniaTheory]
    [InlineData(1, "1 note")]
    [InlineData(4, "4 notes")]
    public void NodeDetail_NotesCount_UsesRealResources(int count, string expected)
        => Assert.Equal(expected, Loc.FormatCount("NodeDetail_NotesCount", count));

    [AvaloniaTheory]
    [InlineData(1, "Opened project 'Foo' (1 patch)")]
    [InlineData(3, "Opened project 'Foo' (3 patches)")]
    public void ProjectOpened_PatchCount_UsesRealResources(int count, string expected)
        => Assert.Equal(expected, Loc.FormatCount("Status_ProjectOpened", count, "Foo"));

    // SharedStrings.axaml lives in a different assembly — prove the provider
    // resolves plural keys from the merged shared dictionary too.
    [AvaloniaTheory]
    [InlineData(1, "Applied 1 patch to Foo.")]
    [InlineData(2, "Applied 2 patches to Foo.")]
    public void PatchManager_ApplySuccess_UsesRealResources(int count, string expected)
        => Assert.Equal(expected, Loc.FormatCount("PatchManager_ApplySuccess", count, "Foo"));
}
