using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class ProjectVoRowScannerTests : IDisposable
{
    private readonly string _projectDir;
    private readonly string _projectPath;
    private readonly string _gameRoot;
    private readonly string _voicesRoot;

    private const string SpeakerGuid = "9c5f12c9-e93d-4952-9f1a-726c9498f8fb";

    public ProjectVoRowScannerTests()
    {
        Loc.Configure(new StubStringProvider());
        _projectDir  = Path.Combine(Path.GetTempPath(), $"BatchScanTest_{Guid.NewGuid():N}");
        _projectPath = Path.Combine(_projectDir, "test.dialogproject");
        _gameRoot    = Path.Combine(_projectDir, "game");
        _voicesRoot  = VoPathResolver.VoicesRoot(_gameRoot);
        Directory.CreateDirectory(_voicesRoot);

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

    /// Plants a game-side .wem so VoPathResolver reports Found for that node.
    private void PlantGameWem(string relative)
    {
        var full = Path.Combine(_voicesRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "");
    }

    private static ConversationNode MakeNode(
        int id, bool hasVO = true, string externalVO = "", string speaker = SpeakerGuid) =>
        new(id, false, SpeakerCategory.Npc, speaker, "", [],
            [], [], "Conversation", "None",
            ActorDirection: "", Comments: "", ExternalVO: externalVO,
            HasVO: hasVO, HideSpeaker: false);

    private static Conversation MakeConv(string name, params ConversationNode[] nodes) =>
        new(name, nodes,
            new StringTable(nodes.Select(n => new StringEntry(n.NodeId, $"line {n.NodeId}", "")).ToList()));

    private static ConversationPatch EmptyPatch(string convName) =>
        new(convName, ConversationPatch.CurrentSchemaVersion, [], [], []);

    private IReadOnlyList<BatchVoRowViewModel> Scan(
        DialogProject project, IGameDataProvider provider,
        string? openName = null, ConversationEditSnapshot? openSnap = null) =>
        ProjectVoRowScanner.BuildRows(
            project, provider, _projectPath, _gameRoot, "poe2", openName, openSnap);

    [Fact]
    public void RowsSpanAllPatchedConversations_SortedByConversationThenNode()
    {
        var provider = new FakeGameDataProvider("poe2", "en",
            MakeConv("bravo", MakeNode(2), MakeNode(1)),
            MakeConv("alpha", MakeNode(7)));
        var project = DialogProject.Empty("P")
            .WithPatch(EmptyPatch("bravo"))
            .WithPatch(EmptyPatch("alpha"));

        var rows = Scan(project, provider);

        Assert.Equal(["alpha", "bravo", "bravo"], rows.Select(r => r.ConversationName));
        Assert.Equal([7, 1, 2], rows.Select(r => r.NodeId));
    }

    [Fact]
    public void NodeWithoutVo_ProducesNoRow()
    {
        var provider = new FakeGameDataProvider("poe2", "en",
            MakeConv("conv", MakeNode(1, hasVO: false), MakeNode(2)));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("conv"));

        var rows = Scan(project, provider);

        var row = Assert.Single(rows);
        Assert.Equal(2, row.NodeId);
    }

    [Fact]
    public void VoStatus_ReflectsGameDisk_AndDestsLandInProjectVoFolder()
    {
        PlantGameWem(Path.Combine("eder", "conv_0001.wem"));   // node 1 Found, node 2 Missing
        var provider = new FakeGameDataProvider("poe2", "en",
            MakeConv("conv", MakeNode(1), MakeNode(2)));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("conv"));

        var rows = Scan(project, provider);

        Assert.Equal(VoPresence.Found,   rows.Single(r => r.NodeId == 1).VoStatus);
        Assert.Equal(VoPresence.Missing, rows.Single(r => r.NodeId == 2).VoStatus);
        var expectedDest = Path.Combine(_projectDir, "_vo", "eder", "conv_0001.wem");
        Assert.Equal(expectedDest, rows.Single(r => r.NodeId == 1).DestPrimaryPath);
        Assert.Equal(expectedDest[..^4] + "_fem.wem", rows.Single(r => r.NodeId == 1).DestFemPath);
    }

    [Fact]
    public void AliasedNode_IsFlaggedAndStillListed()
    {
        var provider = new FakeGameDataProvider("poe2", "en",
            MakeConv("conv", MakeNode(1, hasVO: false, externalVO: "eder/custom_take")));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("conv"));

        var rows = Scan(project, provider);

        var row = Assert.Single(rows);
        Assert.True(row.IsAliased);
    }

    [Fact]
    public void UnreadableConversation_IsSkipped_OthersSurvive()
    {
        var provider = new ThrowingProvider(
            new FakeGameDataProvider("poe2", "en", MakeConv("good", MakeNode(1))),
            throwFor: "bad");
        var project = DialogProject.Empty("P")
            .WithPatch(EmptyPatch("bad"))
            .WithPatch(EmptyPatch("good"));

        var rows = Scan(project, provider);

        var row = Assert.Single(rows);
        Assert.Equal("good", row.ConversationName);
    }

    [Fact]
    public void OpenConversation_UsesLiveSnapshotOverSavedState()
    {
        var provider = new FakeGameDataProvider("poe2", "en",
            MakeConv("conv", MakeNode(1)));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("conv"));
        // Canvas has an extra node 5 that only exists live (unsaved edit).
        var live = ConversationSnapshotBuilder.Build(MakeConv("conv", MakeNode(1), MakeNode(5)));

        var rows = Scan(project, provider, openName: "conv", openSnap: live);

        Assert.Equal([1, 5], rows.Select(r => r.NodeId));
    }

    [Fact]
    public void AddedNode_PreviewFallsBackToPatchTranslationText()
    {
        // A brand-new conversation: no vanilla base, one added node whose text
        // lives only in the patch's translations ([JsonIgnore] on snapshot text).
        var added = new NodeEditSnapshot(
            9, false, SpeakerCategory.Npc, SpeakerGuid, "", "", "",
            "Conversation", "None", "", "", "", true, false, [], [], []);
        var patch = new ConversationPatch("newconv", ConversationPatch.CurrentSchemaVersion,
            [added], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(9, "Fresh new line", "")]
            }
        };
        var provider = new FakeGameDataProvider("poe2", "en");
        var project  = DialogProject.Empty("P").WithPatch(patch);

        var rows = Scan(project, provider);

        var row = Assert.Single(rows);
        Assert.Equal("Fresh new line", row.TextPreview);
    }

    [Fact]
    public void NonPoe2Game_ReturnsNoRows()
    {
        var provider = new FakeGameDataProvider("poe1", "en",
            MakeConv("conv", MakeNode(1)));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("conv"));

        var rows = ProjectVoRowScanner.BuildRows(
            project, provider, _projectPath, _gameRoot, "poe1");

        Assert.Empty(rows);
    }

    /// Delegates to an inner provider but throws on LoadConversation for one
    /// conversation name — simulates an unreadable game file.
    private sealed class ThrowingProvider(FakeGameDataProvider inner, string throwFor) : IGameDataProvider
    {
        public string GameName => inner.GameName;
        public string GameId   => inner.GameId;
        public IReadOnlyList<string> AvailableLanguages => inner.AvailableLanguages;
        public string Language { get => inner.Language; set => inner.Language = value; }

        public IReadOnlyList<ConversationFile> EnumerateConversations() =>
            inner.EnumerateConversations()
                 .Append(inner.BuildNewConversationFile(throwFor)).ToList();

        public Conversation LoadConversation(ConversationFile file) =>
            file.Name == throwFor
                ? throw new IOException($"unreadable: {file.Name}")
                : inner.LoadConversation(file);

        public ConversationFile BuildNewConversationFile(string name) => inner.BuildNewConversationFile(name);
        public IReadOnlyDictionary<string, string> LoadSpeakerNames() => inner.LoadSpeakerNames();
        public void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot) => throw new NotSupportedException();
        public string GetStringTablePath(ConversationFile file) => throw new NotSupportedException();
        public string GetStringTablePath(ConversationFile file, string language) => throw new NotSupportedException();
        public (string ConversationsRoot, string StringTablesRoot) GetBackupRoots() => throw new NotSupportedException();
        public void InitializeConversationFile(ConversationFile file) => throw new NotSupportedException();
    }
}
