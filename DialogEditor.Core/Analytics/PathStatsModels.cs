using DialogEditor.Core.Models;

namespace DialogEditor.Core.Analytics;

/// Total words spoken by one speaker across the conversation, under each reading.
public record SpeakerWordCount(string SpeakerGuid, SpeakerCategory Category, int DefaultWords, int FemaleWords);

/// Stats for one top-level player choice: how much content lives down it, and the
/// longest single read through it, under each reading. Measured from the choice onward.
public record BranchStat(
    int    ChoiceNodeId,
    string ChoiceText,
    int    DefaultContentWords,
    int    DefaultLongestWords,
    int    FemaleContentWords,
    int    FemaleLongestWords);

/// Playthrough-oriented stats for one conversation. Female figures are meaningful only
/// when HasSignificantFemaleVariant is true (else they ~equal the default figures).
public record PathStatsReport(
    bool HasSignificantFemaleVariant,
    int  DefaultTotalWords,
    int  FemaleTotalWords,
    int  DefaultLongestWords,
    int  DefaultShortestWords,
    int  FemaleLongestWords,
    int  FemaleShortestWords,
    IReadOnlyList<SpeakerWordCount> WordsPerSpeaker,
    IReadOnlyList<BranchStat>       Branches);
