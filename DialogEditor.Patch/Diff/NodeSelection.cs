namespace DialogEditor.Patch.Diff;

/// One picked node: which conversation, which node id.
public readonly record struct NodeSelection(string ConversationName, int NodeId);
