using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

public class RefTableTests
{
    private static ElementInfo El(string id, string name) =>
        new(id, name, "Button", "", "", true, false, true, new[] { "Invoke" });

    [Fact]
    public void MintReturnsSequentialReferences()
    {
        var table = new RefTable();
        var refs = table.Mint(new[] { El("a", "One"), El("b", "Two") });
        Assert.Equal(new[] { "ref_1", "ref_2" }, refs);
    }

    [Fact]
    public void ResolveReturnsTheElementIdForAMintedReference()
    {
        var table = new RefTable();
        table.Mint(new[] { El("element-a", "One") });

        Assert.True(table.TryResolve("ref_1", out var id, out _));
        Assert.Equal("element-a", id);
    }

    [Fact]
    public void MintingAgainStartsANewGenerationAndInvalidatesOldReferences()
    {
        var table = new RefTable();
        table.Mint(new[] { El("element-a", "One") });
        table.Mint(new[] { El("element-b", "Two") });

        Assert.False(table.TryResolve("ref_1@1", out _, out var error));
        Assert.Contains("stale", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("read_tree", error);
    }

    [Fact]
    public void GenerationIncrementsOnEachMint()
    {
        var table = new RefTable();
        Assert.Equal(0, table.Generation);
        table.Mint(new[] { El("a", "One") });
        Assert.Equal(1, table.Generation);
    }

    [Fact]
    public void UnknownReferenceIsRejectedWithAHelpfulMessage()
    {
        var table = new RefTable();
        table.Mint(new[] { El("a", "One") });

        Assert.False(table.TryResolve("ref_99", out _, out var error));
        Assert.Contains("ref_99", error);
    }
}
