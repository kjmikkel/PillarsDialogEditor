using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class VoAliasIndexServiceTests : IDisposable
{
    private readonly string _gameRoot;
    private readonly string _baseConvDir;

    public VoAliasIndexServiceTests()
    {
        _gameRoot    = Path.Combine(Path.GetTempPath(), $"VoIdx_{Guid.NewGuid():N}");
        _baseConvDir = Path.Combine(_gameRoot,
            "PillarsOfEternityII_Data", "exported", "design", "conversations", "21_prologue");
        Directory.CreateDirectory(_baseConvDir);
        VoAliasIndexService.Clear();
    }

    public void Dispose()
    {
        VoAliasIndexService.Clear();
        if (Directory.Exists(_gameRoot))
            Directory.Delete(_gameRoot, recursive: true);
    }

    private static string ConvJson(params (int id, string? alias)[] nodes)
    {
        var items = nodes.Select(n =>
            $$"""{ "NodeID": {{n.id}}, "ExternalVO": "{{n.alias ?? ""}}" }""");
        return $$"""{ "Nodes": [ {{string.Join(",", items)}} ] }""";
    }

    [Fact]
    public void Rebuild_IndexesNonEmptyAliases_SkipsEmpty()
    {
        File.WriteAllText(Path.Combine(_baseConvDir, "21_si_test.conversationbundle"),
            ConvJson((1, "narrator/other_conv_0005"), (2, null), (3, "narrator/other_conv_0005")));

        VoAliasIndexService.Rebuild(_gameRoot);

        Assert.True(VoAliasIndexService.IsReady);
        var refs = VoAliasIndexService.GetReferences("narrator/other_conv_0005");
        Assert.Equal(2, refs.Count);
        Assert.Contains(new VoAliasRef("21_si_test", 1), refs);
        Assert.Contains(new VoAliasRef("21_si_test", 3), refs);
        Assert.Empty(VoAliasIndexService.GetReferences("narrator/nothing_0001"));
    }

    [Fact]
    public void Rebuild_KeysAreCaseInsensitive()
    {
        File.WriteAllText(Path.Combine(_baseConvDir, "21_a.conversationbundle"),
            ConvJson((1, "dawnstar_guide/sh_Dawnstar_Guide_0005")));

        VoAliasIndexService.Rebuild(_gameRoot);

        Assert.Single(VoAliasIndexService.GetReferences("DAWNSTAR_GUIDE/SH_DAWNSTAR_GUIDE_0005"));
    }

    [Fact]
    public void Rebuild_OverrideReplacesBaseContribution()
    {
        // Base version of 21_a aliases X twice; the override version only once.
        // Override wins per conversation — X must have exactly one reference.
        File.WriteAllText(Path.Combine(_baseConvDir, "21_a.conversationbundle"),
            ConvJson((1, "narrator/x_0001"), (2, "narrator/x_0001")));
        var overrideDir = Path.Combine(_gameRoot, "override", "SomeMod",
            "design", "conversations", "21_prologue");
        Directory.CreateDirectory(overrideDir);
        File.WriteAllText(Path.Combine(overrideDir, "21_a.conversationbundle"),
            ConvJson((7, "narrator/x_0001")));

        VoAliasIndexService.Rebuild(_gameRoot);

        var refs = VoAliasIndexService.GetReferences("narrator/x_0001");
        Assert.Equal([new VoAliasRef("21_a", 7)], refs);
    }

    [Fact]
    public void Rebuild_MalformedFile_IsSkipped_OthersStillIndexed()
    {
        File.WriteAllText(Path.Combine(_baseConvDir, "21_broken.conversationbundle"),
            "{ not valid json ");
        File.WriteAllText(Path.Combine(_baseConvDir, "21_ok.conversationbundle"),
            ConvJson((4, "narrator/y_0002")));

        VoAliasIndexService.Rebuild(_gameRoot);   // must not throw

        Assert.Single(VoAliasIndexService.GetReferences("narrator/y_0002"));
    }

    [Fact]
    public void GetReferences_BeforeRebuild_NotReadyAndEmpty()
    {
        Assert.False(VoAliasIndexService.IsReady);
        Assert.Empty(VoAliasIndexService.GetReferences("narrator/x_0001"));
    }

    [Fact]
    public void RegisterForTests_SetsReadyAndServesEntries()
    {
        VoAliasIndexService.RegisterForTests(new Dictionary<string, IReadOnlyList<VoAliasRef>>
        {
            ["narrator/z_0009"] = [new VoAliasRef("some_conv", 9)]
        });
        Assert.True(VoAliasIndexService.IsReady);
        Assert.Single(VoAliasIndexService.GetReferences("narrator/z_0009"));
    }
}
