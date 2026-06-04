namespace DialogEditor.Patch.Diff;

/// Attribution for one node: the most recent commit that touched any of the node's
/// lines (structure, text, or translator comment) in the project file at HEAD.
public record NodeBlame(string ConversationName, int NodeId, CommitInfo LastCommit);
