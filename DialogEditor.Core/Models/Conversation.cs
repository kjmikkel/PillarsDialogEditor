namespace DialogEditor.Core.Models;

public record Conversation(
    string Name,
    IReadOnlyList<ConversationNode> Nodes,
    StringTable Strings
);
