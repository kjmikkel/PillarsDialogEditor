using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

/// <summary>
/// Issue #6: dialog-patcher merged several projects with later-wins semantics and never said
/// so when they overlapped. It now prints one line per cross-mod conflict before applying,
/// naming the conversation, node, what clashes, both projects, and which one takes effect.
/// </summary>
public class CrossModConflictReportTests
{
    private static readonly string[] Names = ["ModA", "ModB"];

    [Fact]
    public void FieldConflict_NamesTheFieldBothPacksAndTheWinner()
        => Assert.Equal(
            "conv1, node 5, DefaultText: changed by 'ModA' and 'ModB'; 'ModB' wins (later in the load order)",
            CrossModConflictReport.Describe(new PatchConflict("conv1", 5, "DefaultText", 0, 1), Names));

    [Fact]
    public void DeletionConflict_SaysWhichPackDeletesAndWhichChanges()
        // ConflictDetector puts the deleting project first and the modifying one second.
        => Assert.Equal(
            "conv1, node 7: deleted by 'ModB' but changed by 'ModA'",
            CrossModConflictReport.Describe(new PatchConflict("conv1", 7, null, 1, 0), Names));

    [Fact]
    public void AddedNodeConflict_SaysTheLaterNodeReplacesTheEarlier()
        => Assert.Equal(
            "conv1, node 500: added by both 'ModA' and 'ModB'; the node from 'ModB' replaces the other (later in the load order)",
            CrossModConflictReport.Describe(
                new PatchConflict("conv1", 500, null, 0, 1) { Kind = PatchConflictKind.AddedNode }, Names));

    [Fact]
    public void LinkConflict_NamesBothEndsOfTheLink()
        => Assert.Equal(
            "conv1, link 5 -> 9: changed by 'ModA' and 'ModB'; 'ModB' wins (later in the load order)",
            CrossModConflictReport.Describe(
                new PatchConflict("conv1", 5, null, 0, 1) { Kind = PatchConflictKind.Link, LinkToNodeId = 9 }, Names));
}
