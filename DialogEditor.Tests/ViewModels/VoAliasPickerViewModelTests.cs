using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class VoAliasPickerViewModelTests : IDisposable
{
    private readonly string _gameRoot;

    private sealed class FakeProvider : IGameDataProvider
    {
        public Dictionary<string, Conversation> Convs { get; } = [];
        public string GameName => "Fake"; public string GameId => "poe2";
        public IReadOnlyList<string> AvailableLanguages => ["en"];
        public string Language { get; set; } = "en";
        public IReadOnlyList<ConversationFile> EnumerateConversations()
            => Convs.Keys.Select(BuildNewConversationFile).ToList();
        public Conversation LoadConversation(ConversationFile file) => Convs[file.Name];
        public IReadOnlyDictionary<string, string> LoadSpeakerNames() => new Dictionary<string, string>();
        public void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot) { }
        public string GetStringTablePath(ConversationFile file) => "";
        public string GetStringTablePath(ConversationFile file, string language) => "";
        public (string, string) GetBackupRoots() => ("", "");
        public ConversationFile BuildNewConversationFile(string name)
            => new(name, "", name + ".conversationbundle", "");
        public void InitializeConversationFile(ConversationFile file) { }
    }

    private readonly FakeProvider _provider = new();

    public VoAliasPickerViewModelTests()
    {
        Loc.Configure(new StubStringProvider());
        _gameRoot = Path.Combine(Path.GetTempPath(), $"VoPick_{Guid.NewGuid():N}");
        Directory.CreateDirectory(VoPathResolver.VoicesRoot(_gameRoot));
        ChatterPrefixService.Register(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "eder" }
        });
        SpeakerNameService.Register(new Dictionary<string, string>());
    }

    public void Dispose()
    {
        ChatterPrefixService.Clear();
        SpeakerNameService.Register(new Dictionary<string, string>());
        if (Directory.Exists(_gameRoot)) Directory.Delete(_gameRoot, recursive: true);
    }

    private static ConversationNode Node(int id, string speakerGuid) => new(
        NodeId: id, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
        SpeakerGuid: speakerGuid, ListenerGuid: "",
        Links: [], Conditions: [], Scripts: [],
        DisplayType: "Conversation", Persistence: "None");

    private void AddConv(string name, params ConversationNode[] nodes)
        => _provider.Convs[name] = new Conversation(name, nodes,
            new StringTable(nodes.Select(n => new StringEntry(n.NodeId, $"line {n.NodeId}", ""))));

    [Fact]
    public void SelectingConversation_BuildsRows_WithDerivedAlias()
    {
        AddConv("some_conv", Node(42, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
        var vm = new VoAliasPickerViewModel(_provider, _gameRoot, currentAlias: null);

        vm.SelectedConversation = vm.AllConversations.Single(c => c.Name == "some_conv");

        var row = Assert.Single(vm.VisibleRows);
        Assert.Equal(42, row.NodeId);
        Assert.True(row.IsPickable);
        Assert.Equal(Path.Combine("eder", "some_conv_0042"), row.DerivedAlias);
        Assert.Equal("line 42", row.TextPreview);
        Assert.False(row.WemExists);
    }

    [Fact]
    public void UnknownSpeakerPrefix_RowNotPickable()
    {
        AddConv("some_conv", Node(1, "totally-unknown-guid"));
        var vm = new VoAliasPickerViewModel(_provider, _gameRoot, null);
        vm.SelectedConversation = vm.AllConversations.Single();

        var row = Assert.Single(vm.VisibleRows);
        Assert.False(row.IsPickable);
        Assert.Null(row.DerivedAlias);
    }

    [Fact]
    public void WemExists_ReflectsFileOnDisk()
    {
        AddConv("some_conv", Node(7, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
        var wemDir = Path.Combine(VoPathResolver.VoicesRoot(_gameRoot), "eder");
        Directory.CreateDirectory(wemDir);
        File.WriteAllText(Path.Combine(wemDir, "some_conv_0007.wem"), "");

        var vm = new VoAliasPickerViewModel(_provider, _gameRoot, null);
        vm.SelectedConversation = vm.AllConversations.Single();

        Assert.True(Assert.Single(vm.VisibleRows).WemExists);
    }

    [Fact]
    public void NodeFilter_NarrowsByTextOrId()
    {
        AddConv("some_conv",
            Node(1, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"),
            Node(2, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
        var vm = new VoAliasPickerViewModel(_provider, _gameRoot, null);
        vm.SelectedConversation = vm.AllConversations.Single();

        vm.NodeFilter = "line 2";
        Assert.Equal(2, Assert.Single(vm.VisibleRows).NodeId);
        vm.NodeFilter = "1";
        Assert.Equal(1, Assert.Single(vm.VisibleRows).NodeId);
        vm.NodeFilter = "";
        Assert.Equal(2, vm.VisibleRows.Count);
    }

    [Fact]
    public void ConversationFilter_NarrowsList()
    {
        AddConv("alpha_conv", Node(1, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
        AddConv("beta_conv",  Node(1, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
        var vm = new VoAliasPickerViewModel(_provider, _gameRoot, null);

        vm.ConversationFilter = "beta";
        Assert.Equal("beta_conv", Assert.Single(vm.VisibleConversations).Name);
    }

    [Fact]
    public void CurrentAlias_PreselectsConversationAndRow()
    {
        AddConv("some_conv",
            Node(1, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"),
            Node(9, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
        var vm = new VoAliasPickerViewModel(_provider, _gameRoot, "eder/some_conv_0009");

        Assert.Equal("some_conv", vm.SelectedConversation?.Name);
        Assert.Equal(9, vm.SelectedRow?.NodeId);
        Assert.Equal(Path.Combine("eder", "some_conv_0009"), vm.ResultAlias);
    }

    [Fact]
    public void ResultAlias_NullWithoutSelection()
    {
        AddConv("some_conv", Node(1, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
        var vm = new VoAliasPickerViewModel(_provider, _gameRoot, null);
        Assert.Null(vm.ResultAlias);
    }
}
