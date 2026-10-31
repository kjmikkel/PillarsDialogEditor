using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Conditions;

/// <summary>What a link leads to, as far as the first-passing-link rule cares.</summary>
public enum LinkTargetKind
{
    /// Any non-player-choice target: the conversation goes there.
    Npc,
    /// A player-choice target: shown in the response menu.
    Response,
    /// A bank or question node. It may expand into player responses, so it is never certain
    /// and counts as a possible response.
    Opaque,
}

/// <summary>One link's verdict and target kind. <c>Kind</c> is meaningless for a dangling link (use null instead).</summary>
public readonly record struct ShadowLink(Truth Verdict, LinkTargetKind Kind);

/// <summary>The target-node facts <see cref="LinkShadowing.Describe"/> needs.</summary>
public sealed record ShadowTarget(
    bool IsPlayerChoice, ScriptNodeKind ScriptKind, bool IsQuestionNode,
    string Persistence, IReadOnlyList<ConditionNode> Conditions)
{
    public static ShadowTarget From(NodeEditSnapshot n)
        => new(n.IsPlayerChoice, n.ScriptKind, n.IsQuestionNode, n.Persistence, n.Conditions);
}

/// <summary>
/// The games' first-passing-link rule (issue 353): a node's links are walked in order and the first
/// that passes decides what happens, so a link that always passes hides the links after it.
///
/// PoE2 FlowChart.GetNextNode returns the first link (source PlayType Normal) whose link and
/// target conditions pass, skipping player responses. Conversation.GetResponseNodes walks the
/// links in order too: if the first passing link goes to an NPC/script node it returns no
/// responses; once a player response has passed, later non-response links are skipped and later
/// passing responses are collected. See the design spec, sections 2 and 3.
///
/// Only a link that passes for certain hides anything, so figures may stay upper bounds but
/// never under-count.
/// </summary>
public static class LinkShadowing
{
    /// A bank walks its links using the PlayType of the child that just played, which the editor
    /// cannot know, so a bank source hides nothing; any other source uses its own PlayType.
    public static IReadOnlyList<int?> HiddenBy(
        string sourcePlayType, ScriptNodeKind sourceKind, IReadOnlyList<ShadowLink?> links)
        => sourceKind == ScriptNodeKind.Bank ? new int?[links.Count] : HiddenBy(sourcePlayType, links);

    public static IReadOnlyList<int?> HiddenBy(string sourcePlayType, IReadOnlyList<ShadowLink?> links)
    {
        var hiddenBy = new int?[links.Count];

        // Random/Cycle sources are not first-match; an unrecognised value takes the safe side.
        // Empty is the default and counts as Normal.
        if (sourcePlayType is not ("" or "Normal")) return hiddenBy;

        bool maybeResponse = false;
        int hider = -1;
        LinkTargetKind hiderKind = LinkTargetKind.Npc;
        bool hiderMaybeResponse = false;

        for (int i = 0; i < links.Count; i++)
        {
            if (links[i] is not { } link) continue;            // dangling: neither hides nor is hidden
            if (link.Verdict == Truth.False) continue;         // already pruned elsewhere

            if (hider >= 0)
            {
                // After a certain NPC link only response-capable links (Response, Opaque) can
                // survive, and only if an earlier response might have passed (response mode).
                // After a certain response only later NPC links are unreachable.
                bool hidden = hiderKind == LinkTargetKind.Npc
                    ? link.Kind == LinkTargetKind.Npc || !hiderMaybeResponse
                    : link.Kind == LinkTargetKind.Npc;
                if (hidden) hiddenBy[i] = hider;
            }
            else if (link.Verdict == Truth.True && link.Kind != LinkTargetKind.Opaque)
            {
                hider = i;
                hiderKind = link.Kind;
                hiderMaybeResponse = maybeResponse;
            }

            if (link.Kind != LinkTargetKind.Npc) maybeResponse = true;
        }
        return hiddenBy;
    }

    /// <summary>Null <paramref name="target"/> is a dangling link and yields null.</summary>
    public static ShadowLink? Describe(
        IReadOnlyList<ConditionNode> linkConditions, ShadowTarget? target, GameState state, string gameId)
    {
        if (target is null) return null;

        var kind = target.ScriptKind == ScriptNodeKind.Bank || target.IsQuestionNode ? LinkTargetKind.Opaque
                 : target.IsPlayerChoice ? LinkTargetKind.Response
                 : LinkTargetKind.Npc;

        if (LinkFiring.CannotFire(linkConditions, target.Conditions, state, gameId))
            return new ShadowLink(Truth.False, kind);

        // OncePerConversation / OnceEver targets fail once played, so they are never certain.
        bool persistenceAlwaysPasses = target.Persistence is "" or "None" or "MarkAsRead";
        bool certain = kind != LinkTargetKind.Opaque && persistenceAlwaysPasses
            && ConditionEvaluator.Evaluate(linkConditions, state, gameId) == Truth.True
            && ConditionEvaluator.Evaluate(target.Conditions, state, gameId) == Truth.True;
        return new ShadowLink(certain ? Truth.True : Truth.Unknown, kind);
    }

    public static IReadOnlyList<int?> HiddenBy(
        NodeEditSnapshot source, Func<int, NodeEditSnapshot?> nodeById, GameState state, string gameId)
    {
        var described = new ShadowLink?[source.Links.Count];
        for (int i = 0; i < described.Length; i++)
        {
            var link = source.Links[i];
            var target = nodeById(link.ToNodeId);
            described[i] = Describe(link.Conditions ?? [], target is null ? null : ShadowTarget.From(target), state, gameId);
        }
        return HiddenBy(source.PlayType, source.ScriptKind, described);
    }
}
