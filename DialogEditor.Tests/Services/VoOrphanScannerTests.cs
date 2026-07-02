using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class VoOrphanScannerTests : IDisposable
{
    private readonly string _projectDir;
    private readonly string _voDir;
    private readonly string _projectPath;

    private const string SpeakerGuid = "9c5f12c9-e93d-4952-9f1a-726c9498f8fb";

    public VoOrphanScannerTests()
    {
        _projectDir  = Path.Combine(Path.GetTempPath(), $"OrphanTest_{Guid.NewGuid():N}");
        _voDir       = Path.Combine(_projectDir, "_vo");
        _projectPath = Path.Combine(_projectDir, "test.dialogproject");
        Directory.CreateDirectory(_voDir);

        ChatterPrefixService.Register(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { SpeakerGuid, "eder" }
        });
    }

    public void Dispose()
    {
        ChatterPrefixService.Clear();
        try { Directory.Delete(_projectDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private void PlantWem(string relative)
    {
        var full = Path.Combine(_voDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "");
    }

    private static ConversationNode MakeNode(
        int id, bool hasVO = true, string externalVO = "", string speaker = SpeakerGuid) =>
        new(id, false, SpeakerCategory.Npc, speaker, "", [],
            [], [], "Conversation", "None",
            ActorDirection: "", Comments: "", ExternalVO: externalVO,
            HasVO: hasVO, HideSpeaker: false);

    /// Provider with one conversation "conv" containing the given nodes;
    /// femaleTextIds get non-empty female text in the string table.
    private static FakeGameDataProvider Provider(
        IReadOnlyList<ConversationNode> nodes, params int[] femaleTextIds)
    {
        var entries = nodes.Select(n => new StringEntry(
            n.NodeId, "line", femaleTextIds.Contains(n.NodeId) ? "fem line" : "")).ToList();
        return new FakeGameDataProvider("poe2", "en",
            new Conversation("conv", nodes, new StringTable(entries)));
    }

    /// Project with an (empty) patch for "conv" so the scanner considers it.
    private static DialogProject ProjectWithConvPatch() =>
        DialogProject.Empty("P").WithPatch(
            new ConversationPatch("conv", ConversationPatch.CurrentSchemaVersion, [], [], []));

    [Fact]
    public void ReferencedFile_NotAnOrphan()
    {
        PlantWem(Path.Combine("eder", "conv_0001.wem"));
        var provider = Provider([MakeNode(1)]);

        var orphans = VoOrphanScanner.FindOrphans(ProjectWithConvPatch(), provider, _projectPath);

        Assert.Empty(orphans);
    }

    [Fact]
    public void FileForDeletedNode_IsOrphan()
    {
        PlantWem(Path.Combine("eder", "conv_0001.wem"));
        PlantWem(Path.Combine("eder", "conv_0099.wem"));   // node 99 does not exist
        var provider = Provider([MakeNode(1)]);

        var orphans = VoOrphanScanner.FindOrphans(ProjectWithConvPatch(), provider, _projectPath);

        var orphan = Assert.Single(orphans);
        Assert.EndsWith("conv_0099.wem", orphan);
    }

    [Fact]
    public void FemFileWithoutFemaleText_IsOrphan()
    {
        PlantWem(Path.Combine("eder", "conv_0001.wem"));
        PlantWem(Path.Combine("eder", "conv_0001_fem.wem"));
        var provider = Provider([MakeNode(1)]);   // node 1 has NO female text

        var orphans = VoOrphanScanner.FindOrphans(ProjectWithConvPatch(), provider, _projectPath);

        var orphan = Assert.Single(orphans);
        Assert.EndsWith("_fem.wem", orphan);
    }

    [Fact]
    public void FemFileWithFemaleText_NotAnOrphan()
    {
        PlantWem(Path.Combine("eder", "conv_0001.wem"));
        PlantWem(Path.Combine("eder", "conv_0001_fem.wem"));
        var provider = Provider([MakeNode(1)], femaleTextIds: 1);

        var orphans = VoOrphanScanner.FindOrphans(ProjectWithConvPatch(), provider, _projectPath);

        Assert.Empty(orphans);
    }

    [Fact]
    public void FileForConversationWithoutPatch_IsOrphan()
    {
        // "removedconv" was dropped from the project; its files are orphans.
        PlantWem(Path.Combine("eder", "removedconv_0001.wem"));
        var provider = Provider([MakeNode(1)]);

        var orphans = VoOrphanScanner.FindOrphans(ProjectWithConvPatch(), provider, _projectPath);

        var orphan = Assert.Single(orphans);
        Assert.EndsWith("removedconv_0001.wem", orphan);
    }

    [Fact]
    public void ExternalVoReferencedFile_NotAnOrphan()
    {
        PlantWem(Path.Combine("eder", "custom_take.wem"));
        var provider = Provider([MakeNode(1, hasVO: false, externalVO: "eder/custom_take")]);

        var orphans = VoOrphanScanner.FindOrphans(ProjectWithConvPatch(), provider, _projectPath);

        Assert.Empty(orphans);
    }

    [Fact]
    public void OpenCanvasSnapshot_WinsOverSavedState()
    {
        PlantWem(Path.Combine("eder", "conv_0001.wem"));
        PlantWem(Path.Combine("eder", "conv_0005.wem"));   // node 5 exists only on the canvas
        var provider = Provider([MakeNode(1)]);
        var canvas = ConversationSnapshotBuilder.Build(new Conversation("conv",
            [MakeNode(1), MakeNode(5)],
            new StringTable([new StringEntry(1, "line", ""), new StringEntry(5, "new line", "")])));

        var orphans = VoOrphanScanner.FindOrphans(
            ProjectWithConvPatch(), provider, _projectPath,
            openConversationName: "conv", openSnapshot: canvas);

        Assert.Empty(orphans);
    }
}
