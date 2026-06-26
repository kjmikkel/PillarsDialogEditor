using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class ConversationViewModelBatchVoTests : IDisposable
{
    private const string EderGuid = "9c5f12c9-e93d-4952-9f1a-726c9498f8fb";

    private readonly string _gameRoot;
    private readonly string _projectPath;

    public ConversationViewModelBatchVoTests()
    {
        Loc.Configure(new StubStringProvider());

        _gameRoot    = Path.Combine(Path.GetTempPath(), $"BatchVoTest_{Guid.NewGuid():N}");
        _projectPath = Path.Combine(_gameRoot, "project", "test.dialogproject");
        Directory.CreateDirectory(Path.GetDirectoryName(_projectPath)!);

        ChatterPrefixService.Register(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { EderGuid, "eder" }
        });
    }

    public void Dispose()
    {
        ChatterPrefixService.Clear();
        if (Directory.Exists(_gameRoot))
            Directory.Delete(_gameRoot, recursive: true);
    }

    private static ConversationViewModel MakeVm() => new(new StubDispatcher());

    [Fact]
    public void BuildBatchVoRows_ExcludesNotApplicableNodes()
    {
        var vm = MakeVm();

        var hasVoNode = new ConversationNode(1, false, SpeakerCategory.Npc, EderGuid, "",
            [], [], [], "Conversation", "None", HasVO: true);
        var noVoNode  = new ConversationNode(2, false, SpeakerCategory.Npc, EderGuid, "",
            [], [], [], "Conversation", "None", HasVO: false);

        vm.Load(new Conversation("test_conv", [hasVoNode, noVoNode], StringTable.Empty));
        vm.ProjectPath = _projectPath;

        var rows = vm.BuildBatchVoRows(_gameRoot, "poe2");

        Assert.Single(rows);
        Assert.Equal(1, rows[0].NodeId);
    }

    [Fact]
    public void BuildBatchVoRows_SortsByNodeId()
    {
        var vm = MakeVm();

        var node3 = new ConversationNode(3, false, SpeakerCategory.Npc, EderGuid, "",
            [], [], [], "Conversation", "None", HasVO: true);
        var node1 = new ConversationNode(1, false, SpeakerCategory.Npc, EderGuid, "",
            [], [], [], "Conversation", "None", HasVO: true);

        vm.Load(new Conversation("test_conv", [node3, node1], StringTable.Empty));
        vm.ProjectPath = _projectPath;

        var rows = vm.BuildBatchVoRows(_gameRoot, "poe2");

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].NodeId);
        Assert.Equal(3, rows[1].NodeId);
    }
}
