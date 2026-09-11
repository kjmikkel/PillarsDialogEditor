using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Patch.GitConflict;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// A whole-conversation conflict shows each side as a compact diff summary
/// ("+1 ~2 -0 (3 text)"). GitMergeAnalyzer used to build that sentence itself, but
/// DialogEditor.Patch references only Core and cannot reach Loc — so the analyzer
/// now reports PatchCounts and the row renders them.
/// </summary>
public class ConflictSummaryLabelTests
{
    private static MergeConflict ConversationLevel(PatchCounts mine, PatchCounts theirs) =>
        new(MergeConflictKind.ConversationLevel, "conv", -1, null, "", "")
        { MineCounts = mine, TheirsCounts = theirs };

    [Fact]
    public void ConversationLevelSummaries_ComeFromResources()
    {
        Loc.Configure(new StubStringProvider());
        var row = new ConflictRowViewModel(
            ConversationLevel(new PatchCounts(1, 2, 0, 3), new PatchCounts(4, 5, 6, 7)));

        Assert.Equal("GitConflict_PatchSummary", row.MineValue);
        Assert.Equal("GitConflict_PatchSummary", row.TheirsValue);
    }

    [Fact]
    public void ConflictsWithoutCounts_StillShowTheirRawValues()
    {
        // Every other conflict kind puts real content in MineValue/TheirsValue —
        // a JSON field value or the differing translation — and must pass it through
        // untouched rather than through a resource lookup.
        Loc.Configure(new StubStringProvider());
        var row = new ConflictRowViewModel(new MergeConflict(
            MergeConflictKind.FieldEdit, "conv", 4, "DefaultText", "\"mine\"", "\"theirs\""));

        Assert.Equal("\"mine\"", row.MineValue);
        Assert.Equal("\"theirs\"", row.TheirsValue);
    }
}

public class ConflictSummaryResourceEndToEndTests
{
    public ConflictSummaryResourceEndToEndTests() => Loc.Configure(new AvaloniaStringProvider());

    [AvaloniaFact]
    public void PatchSummary_RendersCountsInArgumentOrder()
    {
        var row = new ConflictRowViewModel(
            new MergeConflict(MergeConflictKind.ConversationLevel, "conv", -1, null, "", "")
            { MineCounts = new PatchCounts(1, 2, 0, 3) });

        Assert.Equal("+1 ~2 -0 (3 text)", row.MineValue);
    }
}
