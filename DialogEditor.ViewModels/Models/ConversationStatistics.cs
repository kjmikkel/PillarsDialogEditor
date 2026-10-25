namespace DialogEditor.ViewModels.Models;

/// Node and word counts for the open canvas. Nodes are counted by their type (SpeakerCategory),
/// the same four categories as the Legend and Flow Analytics (GitHub issue 256).
public record ConversationStatistics(
    int NodeCount,
    int PlayerCount,
    int NpcCount,
    int NarratorCount,
    int ScriptCount,
    int WordCount,
    int FemaleWordCount);
