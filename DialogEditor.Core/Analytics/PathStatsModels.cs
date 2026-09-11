using DialogEditor.Core.Models;

namespace DialogEditor.Core.Analytics;

/// Total words spoken by one speaker across the conversation, under each reading.
public record SpeakerWordCount(string SpeakerGuid, SpeakerCategory Category, int DefaultWords, int FemaleWords);

/// Stats for one player choice: how much content lives down it, and the longest single
/// read through it, under each reading. Measured from the choice onward.
///
/// SubBranches are the next forks the player meets after taking this choice — the same
/// record, recursively, so a nested row means exactly what a top-level one means. A fork
/// that loops back to a choice already on the way here is a leaf: like the DAG's dropped
/// back-edges, a loop to an earlier decision is counted once rather than unrolled.
public record BranchStat(
    NodeRef Choice,
    string ChoiceText,
    int    DefaultContentWords,
    int    DefaultLongestWords,
    int    FemaleContentWords,
    int    FemaleLongestWords,
    IReadOnlyList<BranchStat> SubBranches);

/// One way the conversation can finish: a node with no outgoing links at all.
///
/// Deliberately NOT "a node with no forward edge in the DAG". A node whose every exit
/// loops back to a hub is a DAG terminal — it stops the longest/shortest walk — but the
/// player never experiences it as an ending, and listing it as one would mislead. The
/// cost is that these figures need not reconcile with the header's overall longest,
/// which does stop at DAG terminals; the UI says so.
///
/// Figures are root-to-ending: the longest and shortest read that arrives *here*.
///
/// Second reason these need not reconcile, once conversation handoffs are followed (#14):
/// LongestTo/ShortestTo walk predecessors taking max/min, which under additive spawn
/// semantics under-counts the concurrent case — arriving at an ending in one conversation
/// also entails reading whatever the spawning conversation continued on to, and a
/// max-over-predecessors walk misses those sibling words. Accepted deliberately: making it
/// exact needs per-path spawn sets, for a case FlowIssueKind.ConversationJumpWhileContinuing
/// already reports as an authoring defect.
public record EndingStat(
    NodeRef Node,
    string Text,
    int    DefaultLongestWords,
    int    DefaultShortestWords,
    int    FemaleLongestWords,
    int    FemaleShortestWords);

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
    IReadOnlyList<BranchStat>       Branches,
    IReadOnlyList<EndingStat>       Endings,
    IReadOnlyList<UnfollowedJump>   Unfollowed,
    int                             ConversationsSpanned);
