using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

/// Issue 172: a renumbered node's translations in the other languages live only in the
/// stored patch, and the save drops entries whose node is gone, so they must be re-keyed.
public class NodeIdRemapTests
{
    private static Dictionary<string, IReadOnlyList<NodeTranslation>> Fr(params NodeTranslation[] t) =>
        new() { ["fr"] = t };

    [Fact]
    public void Apply_MovesTheEntryToTheNewId()
    {
        var result = NodeIdRemap.Apply(Fr(new NodeTranslation(4, "salut", "")), [(4, 0)]);
        Assert.Equal(0, Assert.Single(result["fr"]).NodeId);
    }

    [Fact]
    public void Apply_RunsRemapsInOrder()   // renumber, then Undo, before a save
    {
        var result = NodeIdRemap.Apply(Fr(new NodeTranslation(4, "salut", "")), [(4, 0), (0, 4)]);
        Assert.Equal(4, Assert.Single(result["fr"]).NodeId);
    }

    [Fact]
    public void Apply_DropsStaleEntriesAlreadyAtTheTarget()
    {
        // A translation left over from a deleted node 0 must not survive beside the moved one.
        var result = NodeIdRemap.Apply(
            Fr(new NodeTranslation(0, "old", ""), new NodeTranslation(4, "salut", "")), [(4, 0)]);
        var only = Assert.Single(result["fr"]);
        Assert.Equal((0, "salut"), (only.NodeId, only.DefaultText));
    }

    // Review I2: a language with no entry for the moved node still holds the deleted node 0's
    // text under 0; it must not ship on the new start node.
    [Fact]
    public void Apply_DropsTheStaleTarget_EvenWhenTheLanguageLacksTheMovedNode()
    {
        var result = NodeIdRemap.Apply(Fr(new NodeTranslation(0, "old", "")), [(4, 0)]);
        Assert.DoesNotContain(result["fr"], t => t.NodeId == 0);
    }

    // Review I2: renumber then Undo is no change at all, so a node 0 restored by undoing its
    // deletion keeps its own translation.
    [Fact]
    public void Apply_RenumberThenUndo_KeepsTheOriginalNode0()
    {
        var result = NodeIdRemap.Apply(
            Fr(new NodeTranslation(0, "orig", ""), new NodeTranslation(5, "fünf", "")), [(5, 0), (0, 5)]);
        Assert.Contains(result["fr"], t => t.NodeId == 0 && t.DefaultText == "orig");
        Assert.Contains(result["fr"], t => t.NodeId == 5 && t.DefaultText == "fünf");
    }

    [Fact]
    public void Apply_LeavesOtherEntriesAlone()
    {
        var result = NodeIdRemap.Apply(
            Fr(new NodeTranslation(4, "salut", ""), new NodeTranslation(5, "ça va", "")), [(4, 0)]);
        Assert.Contains(result["fr"], t => t.NodeId == 5 && t.DefaultText == "ça va");
    }

    [Fact]
    public void Apply_NoRemaps_ReturnsTheSameEntries()
    {
        var input = Fr(new NodeTranslation(4, "salut", ""));
        Assert.Same(input, NodeIdRemap.Apply(input, []));
    }
}
