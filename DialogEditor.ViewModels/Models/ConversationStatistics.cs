namespace DialogEditor.ViewModels.Models;

public record ConversationStatistics(
    int NodeCount,
    int NpcCount,
    int PlayerCount,
    int WordCount,
    int FemaleWordCount);
