using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Analytics;

/// <summary>
/// GitHub issue 256: the status bar, the canvas toolbar and Flow Analytics gave three different
/// counts for one conversation. The status bar counted the game file without the project's
/// added nodes, the toolbar folded Narrator and Script nodes into "NPC", and the two word
/// counters split differently. All three now count the open canvas, by node type, the same way.
/// </summary>
public class NodeCountConsistencyTests : IDisposable
{
    // Echoes a template's arguments so a test can see what was passed to Loc.Format.
    private sealed class TemplateStrings(Dictionary<string, string> templates) : IStringProvider
    {
        public string Get(string key) => templates.GetValueOrDefault(key, key);
        public bool TryGet(string key, out string value) { value = Get(key); return true; }
    }

    private readonly FakePoe2Game _game = new("en");
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"counts_{Guid.NewGuid():N}.json");
    private readonly string _projectDir = Path.Combine(Path.GetTempPath(), $"counts_proj_{Guid.NewGuid():N}");

    public NodeCountConsistencyTests()
    {
        Loc.Configure(new TemplateStrings(new()
        {
            ["Statistics_Summary"]        = "{0}|{1}|{2}|{3}|{4}|{5}",
            ["Status_ConversationLoaded"] = "{0}|{1}",
            ["Statistics_FemaleDetail"]   = "{0}|{1}",
        }));
        AppSettings.SettingsPathOverride = _settingsPath;
        Directory.CreateDirectory(_projectDir);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        Loc.Configure(new StubStringProvider());
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_projectDir, recursive: true); } catch (Exception) { /* best-effort */ }
        _game.Dispose();
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("  ", 0)]
    [InlineData("one", 1)]
    [InlineData("one  two\tthree\nfour", 4)]
    public void WordCount_SplitsOnAnyWhitespace(string? text, int expected)
        => Assert.Equal(expected, WordCount.Of(text));

    private static NodeViewModel Node(int id, SpeakerCategory category, string text) => new(
        new ConversationNode(id, category == SpeakerCategory.Player, category, "", "", [], [], [], "Conversation", "None"),
        new StringEntry(id, text, ""));

    private static ConversationViewModel Canvas()
    {
        var canvas = new ConversationViewModel(new StubDispatcher());
        canvas.Nodes.Add(Node(1, SpeakerCategory.Player,   "a\tb"));
        canvas.Nodes.Add(Node(2, SpeakerCategory.Npc,      "c d\ne"));
        canvas.Nodes.Add(Node(3, SpeakerCategory.Npc,      "f"));
        canvas.Nodes.Add(Node(4, SpeakerCategory.Narrator, "g"));
        canvas.Nodes.Add(Node(5, SpeakerCategory.Script,   ""));
        canvas.Nodes.Add(Node(6, SpeakerCategory.Script,   ""));
        return canvas;
    }

    [Fact]
    public void Toolbar_And_FlowAnalytics_AgreeOnEveryCount()
    {
        var canvas = Canvas();
        var s    = canvas.Statistics;
        var flow = FlowAnalysisService.Analyze(canvas.BuildSnapshot()).Statistics;

        Assert.Equal((6, 1, 2, 1, 2, 7),
            (s.NodeCount, s.PlayerCount, s.NpcCount, s.NarratorCount, s.ScriptCount, s.WordCount));
        Assert.Equal((flow.TotalNodes, flow.PlayerCount, flow.NpcCount, flow.NarratorCount, flow.ScriptCount, flow.WordCount),
            (s.NodeCount, s.PlayerCount, s.NpcCount, s.NarratorCount, s.ScriptCount, s.WordCount));
    }

    [Fact]
    public void ToolbarText_NamesEveryNodeType()
        => Assert.Equal("6|1|2|1|2|7", Canvas().StatisticsText);

    // The tooltip says what the totals include, and keeps saying it when there is female text.
    [Fact]
    public void ToolbarTooltip_KeepsItsExplanation_WhenAddingTheFemaleWordCount()
    {
        var canvas = Canvas();
        Assert.Equal("ToolTip_Statistics", canvas.StatisticsTooltip);

        canvas.Nodes[0].FemaleText = "her words";
        Assert.Equal("ToolTip_Statistics|2", canvas.StatisticsTooltip);
    }

    [Fact]
    public void Toolbar_UpdatesWhenANodesTypeChanges()
    {
        var canvas = Canvas();
        var raised = new List<string?>();
        canvas.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        canvas.Nodes[1].SetCategory(SpeakerCategory.Narrator);

        Assert.Equal(2, canvas.Statistics.NarratorCount);
        Assert.Contains(nameof(ConversationViewModel.StatisticsText), raised);
    }

    [Fact]
    public void StatusBar_CountsTheNodesOnTheCanvas_IncludingTheProjectsAddedNodes()
    {
        var provider = _game.Provider;
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        Inject(vm, "_provider", provider);
        Inject(vm, "_currentGameDirectory", _game.Root);
        Inject(vm, "_activeGameId", "poe2");
        Inject(vm, "_projectPath", Path.Combine(_projectDir, "proj.dialogproj"));
        typeof(MainWindowViewModel)
            .GetMethod("SetProject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [DialogProject.Empty("Counts").WithPatch(PatchAddingANode())]);

        var file = provider.FindConversation("test_conv")!;
        var vanilla = provider.LoadConversation(file).Nodes.Count;
        typeof(MainWindowViewModel)
            .GetMethod("OnConversationSelected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [file]);

        Assert.Equal(vanilla + 1, vm.Canvas.Nodes.Count);
        Assert.Equal($"test_conv|{vm.Canvas.Nodes.Count}", vm.StatusText);
    }

    private static void Inject(MainWindowViewModel vm, string field, object? value) =>
        typeof(MainWindowViewModel)
            .GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, value);

    private static ConversationPatch PatchAddingANode() =>
        new("test_conv", ConversationPatch.CurrentSchemaVersion,
            [new NodeEditSnapshot(99, false, SpeakerCategory.Npc, "spk", "lst", "", "",
                "Conversation", "None", "", "", "", false, false,
                [new LinkEditSnapshot(99, 1, 1, "", false)], [], [])], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(99, "added line", "")],
            },
        };
}
