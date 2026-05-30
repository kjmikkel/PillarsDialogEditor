namespace DialogEditor.Patch.Diff;

/// A link in the applied result whose target node will not exist after apply.
public readonly record struct DanglingLink(string Conversation, int FromNode, int ToNode);
