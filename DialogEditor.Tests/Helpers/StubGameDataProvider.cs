using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Helpers;

public sealed class StubProvider(
    ConversationFile file, ConversationEditSnapshot snapshot) : IGameDataProvider
{
    public string GameName             => "Stub";
    public string GameId               => "stub";
    public IReadOnlyList<string> AvailableLanguages => [];
    public string Language { get; set; } = "en";

    public ConversationEditSnapshot? SavedSnapshot { get; private set; }

    public IReadOnlyList<ConversationFile> EnumerateConversations() => [file];

    public Conversation LoadConversation(ConversationFile f)
    {
        var nodes = snapshot.Nodes.Select(n => new ConversationNode(
            n.NodeId, n.IsPlayerChoice, n.SpeakerCategory, n.SpeakerGuid, n.ListenerGuid,
            n.Links.Select(l => new NodeLink(l.FromNodeId, l.ToNodeId, l.Conditions ?? [],
                                             l.RandomWeight, l.QuestionNodeTextDisplay)).ToList(),
            n.Conditions, n.Scripts, n.DisplayType, n.Persistence,
            n.ActorDirection, n.Comments, n.ExternalVO, n.HasVO, n.HideSpeaker)).ToList();
        var strings = new StringTable(
            snapshot.Nodes.Select(n => new StringEntry(n.NodeId, n.DefaultText, n.FemaleText)));
        return new Conversation(f.Name, nodes, strings);
    }

    public void SaveConversation(ConversationFile f, ConversationEditSnapshot s)
        => SavedSnapshot = s;

    public IReadOnlyDictionary<string, string> LoadSpeakerNames()
        => new Dictionary<string, string>();
    public string GetStringTablePath(ConversationFile f) => string.Empty;
    public string GetStringTablePath(ConversationFile f, string language) => string.Empty;
    public (string, string) GetBackupRoots() => (string.Empty, string.Empty);
    public ConversationFile BuildNewConversationFile(string name) => file;
    public void InitializeConversationFile(ConversationFile f) { }
}

public sealed class MultiFileProvider(
    IReadOnlyList<(ConversationFile File, ConversationEditSnapshot Snap)> data)
    : IGameDataProvider
{
    public string GameName              => "Stub";
    public string GameId                => "stub";
    public IReadOnlyList<string> AvailableLanguages => [];
    public string Language { get; set; } = "en";

    public IReadOnlyList<ConversationFile> EnumerateConversations()
        => data.Select(d => d.File).ToList();

    public Conversation LoadConversation(ConversationFile f)
    {
        var snap  = data.First(d => d.File.Name == f.Name).Snap;
        var nodes = snap.Nodes.Select(n => new ConversationNode(
            n.NodeId, n.IsPlayerChoice, n.SpeakerCategory, n.SpeakerGuid, n.ListenerGuid,
            n.Links.Select(l => new NodeLink(l.FromNodeId, l.ToNodeId, l.Conditions ?? [],
                                             l.RandomWeight, l.QuestionNodeTextDisplay)).ToList(),
            n.Conditions, n.Scripts, n.DisplayType, n.Persistence,
            n.ActorDirection, n.Comments, n.ExternalVO, n.HasVO, n.HideSpeaker)).ToList();
        var strings = new StringTable(
            snap.Nodes.Select(n => new StringEntry(n.NodeId, n.DefaultText, n.FemaleText)));
        return new Conversation(f.Name, nodes, strings);
    }

    public void SaveConversation(ConversationFile f, ConversationEditSnapshot s) { }
    public IReadOnlyDictionary<string, string> LoadSpeakerNames()
        => new Dictionary<string, string>();
    public string GetStringTablePath(ConversationFile f) => string.Empty;
    public string GetStringTablePath(ConversationFile f, string language) => string.Empty;
    public (string, string) GetBackupRoots() => (string.Empty, string.Empty);
    public ConversationFile BuildNewConversationFile(string name) => data[0].File;
    public void InitializeConversationFile(ConversationFile f) { }
}
