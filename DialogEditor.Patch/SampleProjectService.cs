using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch.Diff;

namespace DialogEditor.Patch;

public sealed class SampleConversationNotFoundException(string conversationName)
    : Exception($"Sample conversation '{conversationName}' was not found in the loaded game.");

public enum SampleSeedResult { Seeded, GitMissing, Partial }

/// One commit's worth of sample state. OnNewExperimentBranch marks where the history forks.
public record SampleCommit(string Message, DialogProject Project, bool OnNewExperimentBranch);

/// The full sample: the file name to write, the final (main-branch) project to open,
/// and the ordered commits used to seed history.
public record SampleBuild(string ProjectFileName, DialogProject Final, IReadOnlyList<SampleCommit> Commits);

/// Builds an install-matched sample .dialogproject from the loaded game and seeds a small
/// git history. Pure and testable (IGitRunner + IGameDataProvider injected); no UI, no logging.
public class SampleProjectService(IGitRunner git)
{
    // ⚠ Confirm these against the loaded game's conversation browser (see plan header).
    public const string Poe1SampleConversation = "companion_eder";   // Eder, Gilded Vale
    public const string Poe2SampleConversation = "companion_eder";   // Eder reunion, Port Maje

    // Sample *content* (data, not UI chrome) — intentionally literal, not localized.
    private const string EditedLineSuffix  = "  (try changing this line!)";
    private const string AltLineSuffix     = "  (an alternate greeting on the experiment branch)";
    private const string NewLineText        = "And this whole line was added as a sample.";
    private const string TranslatorNote     = "Sample translator note: keep Eder's tone warm and informal.";
    private const string Msg1 = "Initial sample — edit Eder's opening line";
    private const string Msg2 = "Reshape the scene — add a line, a note, and trim a dead end";
    private const string Msg3 = "experiment: try an alternate greeting";

    private static string ConversationFor(string gameId) =>
        gameId == "poe2" ? Poe2SampleConversation : Poe1SampleConversation;

    public SampleBuild BuildSample(IGameDataProvider provider)
    {
        var name = ConversationFor(provider.GameId);
        var file = provider.FindConversation(name)
                   ?? throw new SampleConversationNotFoundException(name);

        var conv = provider.LoadConversation(file);
        var lang = provider.Language;
        var baseSnap = ConversationSnapshotBuilder.Build(conv);

        var anchor = baseSnap.Nodes.OrderBy(n => n.NodeId).First();
        int? leafId = baseSnap.Nodes
            .Where(n => n.Links.Count == 0 && n.NodeId != anchor.NodeId)
            .OrderBy(n => n.NodeId)
            .LastOrDefault()?.NodeId;   // a deletable leaf, or null if the conversation has none
        var newId  = NodeIdAllocator.Next(baseSnap.Nodes.Select(n => n.NodeId));

        // ── Version 1 (C1): just the anchor's opening line changed.
        var v1 = WithAnchorText(baseSnap, anchor.NodeId, anchor.DefaultText + EditedLineSuffix);
        var p1 = ProjectFrom(name, baseSnap, v1, lang, withNote: false);

        // ── Version 2 (C2, = Final): add a node + link, remove the leaf, add a translator note.
        var newNode = new NodeEditSnapshot(
            newId, false, SpeakerCategory.Npc, "", "", NewLineText, "",
            anchor.DisplayType, anchor.Persistence, "", "", "", false, false, [], [], []);

        var v2Nodes = v1.Nodes
            .Select(n => n.NodeId == anchor.NodeId
                ? n with { Links = [.. n.Links, new LinkEditSnapshot(anchor.NodeId, newId, 1f, "", false)] }
                : n)
            .Where(n => leafId is null || n.NodeId != leafId)
            .Select(n => leafId is null ? n
                : n with { Links = n.Links.Where(l => l.ToNodeId != leafId).ToList() })
            .Append(newNode)
            .ToList();
        var v2 = new ConversationEditSnapshot(v2Nodes);
        var p2 = ProjectFrom(name, baseSnap, v2, lang, withNote: true, noteNodeId: anchor.NodeId);

        // ── Version 3 (C3, experiment): an alternate anchor greeting.
        var v3 = WithAnchorText(v2, anchor.NodeId, anchor.DefaultText + AltLineSuffix);
        var p3 = ProjectFrom(name, baseSnap, v3, lang, withNote: true, noteNodeId: anchor.NodeId);

        var fileName = provider.GameId == "poe2"
            ? "sample-poe2.dialogproject"
            : "sample-poe1.dialogproject";

        var commits = new List<SampleCommit>
        {
            new(Msg1, p1, OnNewExperimentBranch: false),
            new(Msg2, p2, OnNewExperimentBranch: false),
            new(Msg3, p3, OnNewExperimentBranch: true),
        };
        return new SampleBuild(fileName, p2, commits);
    }

    private static ConversationEditSnapshot WithAnchorText(
        ConversationEditSnapshot snap, int anchorId, string text) =>
        new(snap.Nodes.Select(n => n.NodeId == anchorId ? n with { DefaultText = text } : n).ToList());

    private static DialogProject ProjectFrom(
        string name, ConversationEditSnapshot baseSnap, ConversationEditSnapshot current,
        string lang, bool withNote, int noteNodeId = 0)
    {
        var patch = DiffEngine.Diff(name, baseSnap, current, lang);
        if (withNote)
            patch = patch with
            {
                NodeComments = new Dictionary<int, string> { [noteNodeId] = TranslatorNote }
            };
        return DialogProject.Empty("Sample").WithPatch(patch);
    }
}
