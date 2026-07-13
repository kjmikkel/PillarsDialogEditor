using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class ProjectFindServiceTests
{
    private const string SpeakerGuid = "9c5f12c9-e93d-4952-9f1a-726c9498f8fb";

    public ProjectFindServiceTests()
    {
        Loc.Configure(new StubStringProvider());
    }

    [Fact] // Default text match, primary language label ""
    public void FindsDefaultText_PrimaryLanguage()
    {
        var (project, provider) = ProjectWith(conv: "c1", nodeId: 1, defaultText: "The Watcher speaks");
        var rows = ProjectFindService.Search(project, provider, "en",
            new ProjectFindQuery("Watcher"));
        var row = Assert.Single(rows);
        Assert.Equal("c1", row.ConversationName);
        Assert.Equal(1, row.NodeId);
        Assert.Equal("", row.Language);
        Assert.Contains("Watcher", row.Snippet);
    }

    [Fact] // Case sensitivity
    public void CaseSensitive_DoesNotMatchDifferentCase()
    {
        var (project, provider) = ProjectWith("c1", 1, "The Watcher");
        Assert.Empty(ProjectFindService.Search(project, provider, "en",
            new ProjectFindQuery("watcher", CaseSensitive: true)));
        Assert.Single(ProjectFindService.Search(project, provider, "en",
            new ProjectFindQuery("watcher", CaseSensitive: false)));
    }

    [Fact] // Link/choice text only when toggled
    public void LinkChoiceText_OnlyWhenToggled()
    {
        var (project, provider) = ProjectWithLink("c1", 1, linkText: "Ask about Caed Nua");
        Assert.Empty(ProjectFindService.Search(project, provider, "en",
            new ProjectFindQuery("Caed Nua")));                         // off by default
        Assert.Single(ProjectFindService.Search(project, provider, "en",
            new ProjectFindQuery("Caed Nua", InLinkChoice: true)));
    }

    [Fact] // Node comment only when toggled; comes from patch.NodeComments
    public void NodeComment_OnlyWhenToggled()
    {
        var (project, provider) = ProjectWithComment("c1", 1, comment: "revisit this line");
        Assert.Empty(ProjectFindService.Search(project, provider, "en",
            new ProjectFindQuery("revisit")));
        Assert.Single(ProjectFindService.Search(project, provider, "en",
            new ProjectFindQuery("revisit", InNodeComments: true)));
    }

    [Fact] // Translation language labeled; primary not duplicated
    public void Translation_LabeledByLanguage_WhenToggled()
    {
        var (project, provider) = ProjectWithTranslation("c1", 1, lang: "de", text: "Der Wächter");
        var rows = ProjectFindService.Search(project, provider, "en",
            new ProjectFindQuery("Wächter", InTranslations: true));
        var row = Assert.Single(rows);
        Assert.Equal("de", row.Language);
    }

    [Fact] // Open conversation uses the passed live snapshot (unsaved edit), not disk
    public void OpenConversation_UsesLiveSnapshot()
    {
        var (project, provider) = ProjectWith("c1", 1, defaultText: "on disk");
        var live = SnapshotWith(nodeId: 1, defaultText: "unsaved edit XYZ");
        var rows = ProjectFindService.Search(project, provider, "en",
            new ProjectFindQuery("XYZ"), openConversationName: "c1", openSnapshot: live);
        Assert.Single(rows);
    }

    [Fact] // Unreadable conversation is skipped, others returned
    public void UnreadableConversation_Skipped()
    {
        var (project, provider) = TwoConvsOneThrows(good: "c1", bad: "c2", text: "findme");
        var rows = ProjectFindService.Search(project, provider, "en",
            new ProjectFindQuery("findme"));
        Assert.All(rows, r => Assert.Equal("c1", r.ConversationName));
    }

    // -- helpers -----------------------------------------------------------

    private static ConversationNode MakeNode(int id, IReadOnlyList<NodeLink>? links = null) =>
        new(id, false, SpeakerCategory.Npc, SpeakerGuid, "", links ?? [], [], [], "Conversation", "None");

    private static ConversationPatch EmptyPatch(string convName) =>
        new(convName, ConversationPatch.CurrentSchemaVersion, [], [], []);

    private static (DialogProject, IGameDataProvider) ProjectWith(string conv, int nodeId, string defaultText)
    {
        var node = MakeNode(nodeId);
        var strings = new StringTable([new StringEntry(nodeId, defaultText, "")]);
        var convObj = new Conversation(conv, [node], strings);
        var provider = new FakeGameDataProvider("poe2", "en", convObj);
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch(conv));
        return (project, provider);
    }

    private static (DialogProject, IGameDataProvider) ProjectWithLink(string conv, int nodeId, string linkText)
    {
        var link = new NodeLink(nodeId, nodeId + 1, [], 1f, linkText);
        var node = MakeNode(nodeId, [link]);
        var strings = new StringTable([new StringEntry(nodeId, "unrelated text", "")]);
        var convObj = new Conversation(conv, [node], strings);
        var provider = new FakeGameDataProvider("poe2", "en", convObj);
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch(conv));
        return (project, provider);
    }

    private static (DialogProject, IGameDataProvider) ProjectWithComment(string conv, int nodeId, string comment)
    {
        var node = MakeNode(nodeId);
        var strings = new StringTable([new StringEntry(nodeId, "unrelated text", "")]);
        var convObj = new Conversation(conv, [node], strings);
        var provider = new FakeGameDataProvider("poe2", "en", convObj);
        var patch = EmptyPatch(conv) with
        {
            NodeComments = new Dictionary<int, string> { [nodeId] = comment }
        };
        var project = DialogProject.Empty("P").WithPatch(patch);
        return (project, provider);
    }

    private static (DialogProject, IGameDataProvider) ProjectWithTranslation(
        string conv, int nodeId, string lang, string text)
    {
        var node = MakeNode(nodeId);
        var strings = new StringTable([new StringEntry(nodeId, "unrelated text", "")]);
        var convObj = new Conversation(conv, [node], strings);
        var provider = new FakeGameDataProvider("poe2", "en", convObj);
        var patch = EmptyPatch(conv) with
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                [lang] = [new NodeTranslation(nodeId, text, "")]
            }
        };
        var project = DialogProject.Empty("P").WithPatch(patch);
        return (project, provider);
    }

    private static ConversationEditSnapshot SnapshotWith(int nodeId, string defaultText) =>
        new([new NodeEditSnapshot(
            nodeId, false, SpeakerCategory.Npc, SpeakerGuid, "", defaultText, "",
            "Conversation", "None", "", "", "", true, false, [], [], [])]);

    private static (DialogProject, IGameDataProvider) TwoConvsOneThrows(string good, string bad, string text)
    {
        var goodNode = MakeNode(1);
        var goodStrings = new StringTable([new StringEntry(1, text, "")]);
        var goodConv = new Conversation(good, [goodNode], goodStrings);

        var inner = new FakeGameDataProvider("poe2", "en", goodConv);
        var provider = new ThrowingProvider(inner, throwFor: bad);

        var project = DialogProject.Empty("P")
            .WithPatch(EmptyPatch(bad))
            .WithPatch(EmptyPatch(good));

        return (project, provider);
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
