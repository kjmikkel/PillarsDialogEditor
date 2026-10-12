using System.ComponentModel;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Issue 167: the detail pane warns, while the author edits, about scripts on a Deadfire
/// script node that the game never runs. The rule itself is pinned in FlowAnalysisServiceTests
/// (ScriptNodeRules); these tests pin that the pane shows it and keeps it current.
public class NodeDetailViewModelScriptWarningsTests
{
    private const string ExitKey   = "NodeDetail_ScriptWarning_ExitNeverRuns";
    private const string UpdateKey = "NodeDetail_ScriptWarning_UpdateNeverRuns";

    public NodeDetailViewModelScriptWarningsTests() => Loc.Configure(new StubStringProvider());

    private static ScriptCall Script(ScriptCategory category) =>
        new("Void SetGlobalValue(String, Int32)", ["g", "1"], category);

    private static NodeViewModel ScriptNode(params ScriptCall[] scripts) =>
        new(new ConversationNode(4, false, SpeakerCategory.Script, "", "", [], [], scripts,
                                 "Conversation", "None"),
            new StringEntry(4, "", ""));

    private static ConnectionViewModel Conn() =>
        new(new ConnectorViewModel(), new ConnectorViewModel(), "", 1, null);

    private static NodeDetailViewModel Pane(string gameId, NodeViewModel node, int links)
    {
        var vm = new NodeDetailViewModel { ActiveGameId = gameId };
        vm.Load(node);
        vm.RefreshLinks(Enumerable.Range(0, links).Select(_ => Conn()));
        return vm;
    }

    [Fact]
    public void Poe2ScriptNode_ThatLeadsOn_WarnsAboutItsExitScripts()
    {
        var vm = Pane("poe2", ScriptNode(Script(ScriptCategory.Exit)), links: 1);
        Assert.Equal([ExitKey], vm.ScriptWarnings);   // StubStringProvider echoes keys
    }

    [Fact]
    public void Poe2ScriptNode_WarnsAboutUpdateScripts()
    {
        var vm = Pane("poe2", ScriptNode(Script(ScriptCategory.Update)), links: 0);
        Assert.Equal([UpdateKey], vm.ScriptWarnings);
    }

    [Fact]
    public void Poe1ScriptNode_HasNoWarnings()
    {
        var vm = Pane("poe1", ScriptNode(Script(ScriptCategory.Exit), Script(ScriptCategory.Update)), links: 1);
        Assert.Empty(vm.ScriptWarnings);
    }

    [Fact]
    public void NoNodeLoaded_HasNoWarnings()
    {
        var vm = new NodeDetailViewModel { ActiveGameId = "poe2" };
        Assert.Empty(vm.ScriptWarnings);
    }

    [Fact]
    public void AddingALink_RaisesTheExitWarning()
    {
        var vm = Pane("poe2", ScriptNode(Script(ScriptCategory.Exit)), links: 0);
        Assert.Empty(vm.ScriptWarnings);
        var raised = Track(vm);

        vm.RefreshLinks([Conn()]);

        Assert.Contains(nameof(NodeDetailViewModel.ScriptWarnings), raised);
        Assert.Equal([ExitKey], vm.ScriptWarnings);
    }

    [Fact]
    public void EditingTheScripts_RefreshesTheWarnings()
    {
        var node = ScriptNode();
        var vm   = Pane("poe2", node, links: 1);
        var raised = Track(vm);

        node.Scripts = [Script(ScriptCategory.Exit)];

        Assert.Contains(nameof(NodeDetailViewModel.ScriptWarnings), raised);
        Assert.Equal([ExitKey], vm.ScriptWarnings);
    }

    [Fact]
    public void SwitchingGame_RefreshesTheWarnings()
    {
        var vm = Pane("poe1", ScriptNode(Script(ScriptCategory.Update)), links: 0);
        var raised = Track(vm);

        vm.ActiveGameId = "poe2";

        Assert.Contains(nameof(NodeDetailViewModel.ScriptWarnings), raised);
        Assert.Equal([UpdateKey], vm.ScriptWarnings);
    }

    private static List<string?> Track(INotifyPropertyChanged vm)
    {
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        return raised;
    }
}
