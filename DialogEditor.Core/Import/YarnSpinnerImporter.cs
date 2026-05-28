using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Import;

public class YarnSpinnerImporter : IDialogImporter
{
    public string[] FileExtensions => [".yarn"];

    public ImportedConversation Import(string path)
    {
        var lines = File.ReadAllLines(path);

        var rawBlocks = ParseBlocks(lines);

        if (rawBlocks.Count == 0)
            throw new FormatException("Yarn file is empty or contains no valid blocks.");

        // First pass: generate nodes for every block and build title → first-node-id map.
        var allPendingNodes = new List<PendingNode>();
        var blockFirstNodeId = new Dictionary<string, int>(StringComparer.Ordinal);
        int nextId = 1;

        foreach (var block in rawBlocks)
        {
            int firstId = nextId;
            var pending = GeneratePendingNodes(block, ref nextId);
            if (pending.Count > 0)
                blockFirstNodeId[block.Title] = firstId;
            allPendingNodes.AddRange(pending);
        }

        // Second pass: resolve [[JumpTarget]] links using the title → first-node map.
        var nodes = new List<NodeEditSnapshot>();
        var texts = new List<NodeTranslation>();

        foreach (var pending in allPendingNodes)
        {
            var links = ResolveLinks(pending, blockFirstNodeId);
            var node = new NodeEditSnapshot(
                NodeId: pending.NodeId,
                IsPlayerChoice: pending.IsPlayerChoice,
                SpeakerCategory: pending.SpeakerCategory,
                SpeakerGuid: "",
                ListenerGuid: "",
                DefaultText: pending.DefaultText,
                FemaleText: "",
                DisplayType: "Conversation",
                Persistence: "None",
                ActorDirection: "",
                Comments: "",
                ExternalVO: "",
                HasVO: false,
                HideSpeaker: false,
                Links: links,
                Conditions: [],
                Scripts: []);

            nodes.Add(node);
            texts.Add(new NodeTranslation(pending.NodeId, pending.DefaultText, ""));
        }

        var warnings = TallySkippedConstructs(rawBlocks);
        var name = Path.GetFileNameWithoutExtension(path);
        return new ImportedConversation(name, nodes, texts, warnings);
    }

    // ── Block parsing ─────────────────────────────────────────────────────

    private sealed record RawBlock(string Title, IReadOnlyList<string> BodyLines);

    private static List<RawBlock> ParseBlocks(string[] lines)
    {
        var blocks = new List<RawBlock>();
        int i = 0;

        while (i < lines.Length)
        {
            // Scan for the start of a metadata section — look for a "title:" header.
            string? title = null;
            while (i < lines.Length)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                    title = trimmed["title:".Length..].Trim();
                else if (trimmed == "---")
                {
                    i++;
                    break;
                }
                i++;
            }

            if (title is null)
                continue;

            // Collect body lines until "===".
            var body = new List<string>();
            while (i < lines.Length)
            {
                var trimmed = lines[i].Trim();
                if (trimmed == "===")
                {
                    i++;
                    break;
                }
                body.Add(trimmed);
                i++;
            }

            blocks.Add(new RawBlock(title, body));
        }

        return blocks;
    }

    // ── Node generation ───────────────────────────────────────────────────

    // Holds enough information to build the final NodeEditSnapshot once link
    // targets are known.
    private sealed class PendingNode
    {
        public int NodeId { get; init; }
        public bool IsPlayerChoice { get; init; }
        public SpeakerCategory SpeakerCategory { get; init; }
        public string DefaultText { get; init; } = "";

        // Inline links to already-known node IDs (e.g. next sequential NPC line).
        public List<int> DirectLinks { get; } = [];

        // Jump targets that must be resolved via the title map.
        public List<string> JumpTargets { get; } = [];
    }

    private static List<PendingNode> GeneratePendingNodes(RawBlock block, ref int nextId)
    {
        // Collect all content lines, skipping commands and comments.
        var contentLines = new List<(bool IsChoice, string Text, string? JumpTarget)>();

        foreach (var raw in block.BodyLines)
        {
            if (raw.StartsWith("<<", StringComparison.Ordinal))
                continue;
            if (raw.StartsWith("//", StringComparison.Ordinal))
                continue;
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            if (raw.StartsWith("->", StringComparison.Ordinal))
            {
                var choiceText = raw[2..].Trim();
                string? jumpTarget = null;
                var jumpStart = choiceText.LastIndexOf("[[", StringComparison.Ordinal);
                if (jumpStart >= 0 && choiceText.EndsWith("]]", StringComparison.Ordinal))
                {
                    jumpTarget = choiceText[(jumpStart + 2)..^2].Trim();
                    choiceText = choiceText[..jumpStart].Trim();
                }
                contentLines.Add((true, choiceText, jumpTarget));
            }
            else
            {
                // Speaker: text  or  plain text (no speaker prefix)
                var colonIdx = raw.IndexOf(':', StringComparison.Ordinal);
                string dialogText;
                if (colonIdx > 0)
                    dialogText = raw[(colonIdx + 1)..].Trim();
                else
                    dialogText = raw;

                contentLines.Add((false, dialogText, null));
            }
        }

        if (contentLines.Count == 0)
            return [];

        // Separate NPC/narrator lines from choice lines.  All choices in a
        // block are gathered after the last NPC line; they share a parent.
        // Handle interleaved structures by grouping runs: a run of NPC lines
        // followed by any choices that immediately follow them.
        var pending = new List<PendingNode>();

        // Find the index where choices begin (choices always come at the end
        // in well-formed Yarn; accept that assumption here).
        int choiceStart = contentLines.Count;
        for (int j = 0; j < contentLines.Count; j++)
        {
            if (contentLines[j].IsChoice)
            {
                choiceStart = j;
                break;
            }
        }

        // Build NPC nodes for non-choice lines.
        var npcLineIds = new List<int>();
        for (int j = 0; j < choiceStart; j++)
        {
            var (_, text, _) = contentLines[j];
            var speakerCategory = ResolveSpeakerCategory(block.BodyLines, j);
            var p = new PendingNode
            {
                NodeId = nextId++,
                IsPlayerChoice = speakerCategory == SpeakerCategory.Player,
                SpeakerCategory = speakerCategory,
                DefaultText = text,
            };
            pending.Add(p);
            npcLineIds.Add(p.NodeId);
        }

        // Chain sequential NPC/narrator nodes together.
        for (int j = 0; j < npcLineIds.Count - 1; j++)
            pending[j].DirectLinks.Add(npcLineIds[j + 1]);

        // Build choice nodes.
        var choiceIds = new List<int>();
        for (int j = choiceStart; j < contentLines.Count; j++)
        {
            var (_, text, jumpTarget) = contentLines[j];
            var p = new PendingNode
            {
                NodeId = nextId++,
                IsPlayerChoice = true,
                SpeakerCategory = SpeakerCategory.Player,
                DefaultText = text,
            };
            if (jumpTarget is not null)
                p.JumpTargets.Add(jumpTarget);
            pending.Add(p);
            choiceIds.Add(p.NodeId);
        }

        // The last NPC node in the block links to all choices.
        if (choiceIds.Count > 0 && npcLineIds.Count > 0)
        {
            var lastNpc = pending[npcLineIds.Count - 1];
            lastNpc.DirectLinks.AddRange(choiceIds);
        }

        return pending;
    }

    // ── Speaker category helpers ──────────────────────────────────────────

    private static SpeakerCategory ResolveSpeakerCategory(IReadOnlyList<string> bodyLines, int contentLineIndex)
    {
        // Walk through non-skipped, non-choice body lines to find the nth one.
        int count = 0;
        foreach (var raw in bodyLines)
        {
            if (raw.StartsWith("<<", StringComparison.Ordinal)) continue;
            if (raw.StartsWith("//", StringComparison.Ordinal)) continue;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (raw.StartsWith("->", StringComparison.Ordinal)) continue;

            if (count == contentLineIndex)
                return SpeakerCategoryFromLine(raw);
            count++;
        }
        return SpeakerCategory.Npc;
    }

    private static SpeakerCategory SpeakerCategoryFromLine(string raw)
    {
        var colonIdx = raw.IndexOf(':', StringComparison.Ordinal);
        if (colonIdx <= 0)
            return SpeakerCategory.Npc;

        var speakerName = raw[..colonIdx].Trim();
        if (speakerName.Equals("player", StringComparison.OrdinalIgnoreCase))
            return SpeakerCategory.Player;
        if (speakerName.Equals("narrator", StringComparison.OrdinalIgnoreCase))
            return SpeakerCategory.Narrator;
        return SpeakerCategory.Npc;
    }

    // ── Skipped-construct tallying ────────────────────────────────────────

    // Counts each distinct <<keyword>> across all block bodies. The keyword is the
    // run of characters after "<<", stopping at the first whitespace or ">>".
    private static List<ImportWarning> TallySkippedConstructs(IReadOnlyList<RawBlock> blocks)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var block in blocks)
        {
            foreach (var raw in block.BodyLines)
            {
                if (!raw.StartsWith("<<", StringComparison.Ordinal))
                    continue;

                var keyword = ExtractKeyword(raw);
                if (keyword.Length == 0)
                    continue;

                counts[keyword] = counts.GetValueOrDefault(keyword) + 1;
            }
        }

        return counts.Select(kv => new ImportWarning(kv.Key, kv.Value)).ToList();
    }

    // "<<if $gold > 10>>" -> "if";  "<<endif>>" -> "endif";  "<<>>" -> "".
    private static string ExtractKeyword(string line)
    {
        int start = 2; // skip "<<"
        int i = start;
        while (i < line.Length
               && !char.IsWhiteSpace(line[i])
               && line[i] != '>')
        {
            i++;
        }
        return line[start..i];
    }

    // ── Link resolution ───────────────────────────────────────────────────

    private static List<LinkEditSnapshot> ResolveLinks(
        PendingNode pending,
        IReadOnlyDictionary<string, int> blockFirstNodeId)
    {
        var links = new List<LinkEditSnapshot>();

        foreach (var toId in pending.DirectLinks)
            links.Add(MakeLink(pending.NodeId, toId));

        foreach (var target in pending.JumpTargets)
        {
            if (blockFirstNodeId.TryGetValue(target, out int toId))
                links.Add(MakeLink(pending.NodeId, toId));
            // Dangling jump — silently skip per spec.
        }

        return links;
    }

    private static LinkEditSnapshot MakeLink(int fromId, int toId) =>
        new(FromNodeId: fromId,
            ToNodeId: toId,
            RandomWeight: 1f,
            QuestionNodeTextDisplay: "",
            HasConditions: false)
        {
            Conditions = null
        };
}
