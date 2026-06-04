using DialogEditor.Patch.Diff;

namespace DialogEditor.ViewModels;

/// One attribution row. Presentation-free Date (DateTimeOffset); the view formats it.
public class NodeBlameRowViewModel(NodeBlame blame)
{
    public string         ConversationName => blame.ConversationName;
    public int            NodeId           => blame.NodeId;
    public string         Author           => blame.LastCommit.Author;
    public DateTimeOffset Date             => blame.LastCommit.Date;
    public string         ShortSha         => blame.LastCommit.ShortSha;
    public string         Subject          => blame.LastCommit.Subject;
}
