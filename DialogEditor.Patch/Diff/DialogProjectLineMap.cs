using System.Text;
using System.Text.Json;

namespace DialogEditor.Patch.Diff;

/// Maps each node to the 1-based inclusive line ranges it occupies in a serialized
/// .dialogproject: its AddedNodes/ModifiedNodes object, its Translations[lang] entries,
/// and its NodeComments entry. Pure — operates on the exact bytes git blame ran against,
/// so line numbers line up with blame output. DeletedNodeIds and Layouts are intentionally
/// out of scope (a deleted node has no surviving lines; layout-only edits aren't attributed).
public static class DialogProjectLineMap
{
    private enum Role
    {
        Other, Root, PatchesObj, ConversationObj,
        AddedNodesArr, ModifiedNodesArr, NodeObj,
        TranslationsObj, TranslationLangArr, NodeCommentsObj,
    }

    private sealed class Frame
    {
        public Role    Role;
        public int     StartLine;
        public string? Conv;
        public int?    NodeId;
        public string? PendingProp;
        public int     PendingPropLine;
    }

    public static IReadOnlyDictionary<(string Conv, int NodeId), IReadOnlyList<(int Start, int End)>>
        Build(string projectJson)
    {
        var bytes = StripBom(Encoding.UTF8.GetBytes(projectJson));
        var lineStarts = BuildLineStarts(bytes);
        int LineOf(long offset) => LineOfOffset(lineStarts, (int)offset);

        var ranges = new Dictionary<(string, int), List<(int, int)>>();
        void Record(string conv, int nodeId, int start, int end)
        {
            if (!ranges.TryGetValue((conv, nodeId), out var list))
                ranges[(conv, nodeId)] = list = [];
            list.Add((start, end));
        }

        var stack = new Stack<Frame>();
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions());

        while (reader.Read())
        {
            var line = LineOf(reader.TokenStartIndex);
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                {
                    var parent = stack.Count > 0 ? stack.Peek() : null;
                    var role   = ChildObjectRole(parent);
                    var conv   = role == Role.ConversationObj ? parent!.PendingProp : parent?.Conv;
                    stack.Push(new Frame { Role = role, StartLine = line, Conv = conv });
                    break;
                }
                case JsonTokenType.StartArray:
                {
                    var parent = stack.Count > 0 ? stack.Peek() : null;
                    stack.Push(new Frame { Role = ChildArrayRole(parent), StartLine = line, Conv = parent?.Conv });
                    break;
                }
                case JsonTokenType.PropertyName:
                {
                    var top = stack.Peek();
                    top.PendingProp     = reader.GetString();
                    top.PendingPropLine = line;
                    break;
                }
                case JsonTokenType.EndObject:
                {
                    var frame = stack.Pop();
                    if (frame.Role == Role.NodeObj && frame.NodeId is int id && frame.Conv is string c)
                        Record(c, id, frame.StartLine, line);
                    break;
                }
                case JsonTokenType.EndArray:
                    stack.Pop();
                    break;

                // ── value tokens ──────────────────────────────────────────────
                case JsonTokenType.Number:
                {
                    var top = stack.Peek();
                    if (top.Role == Role.NodeObj && top.PendingProp == "NodeId")
                        top.NodeId = reader.GetInt32();
                    break;
                }
                case JsonTokenType.String:
                {
                    var top = stack.Peek();
                    if (top.Role == Role.NodeCommentsObj
                        && int.TryParse(top.PendingProp, out var commentNodeId)
                        && top.Conv is string conv)
                        Record(conv, commentNodeId, top.PendingPropLine, line);
                    break;
                }
            }
        }

        return ranges.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<(int, int)>)kvp.Value.OrderBy(r => r.Item1).ToList());
    }

    private static Role ChildObjectRole(Frame? parent) => parent?.Role switch
    {
        null                                              => Role.Root,
        Role.Root        when parent.PendingProp == "Patches"      => Role.PatchesObj,
        Role.PatchesObj                                            => Role.ConversationObj,
        Role.ConversationObj when parent.PendingProp == "Translations" => Role.TranslationsObj,
        Role.ConversationObj when parent.PendingProp == "NodeComments" => Role.NodeCommentsObj,
        Role.AddedNodesArr or Role.ModifiedNodesArr or Role.TranslationLangArr => Role.NodeObj,
        _                                                          => Role.Other,
    };

    private static Role ChildArrayRole(Frame? parent) => parent?.Role switch
    {
        Role.ConversationObj when parent.PendingProp == "AddedNodes"    => Role.AddedNodesArr,
        Role.ConversationObj when parent.PendingProp == "ModifiedNodes" => Role.ModifiedNodesArr,
        Role.TranslationsObj                                            => Role.TranslationLangArr,
        _                                                               => Role.Other,
    };

    private static byte[] StripBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes[3..]
            : bytes;

    private static int[] BuildLineStarts(byte[] bytes)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < bytes.Length; i++)
            if (bytes[i] == (byte)'\n') starts.Add(i + 1);
        return [.. starts];
    }

    // Largest line whose start offset is <= the token offset; 1-based.
    private static int LineOfOffset(int[] lineStarts, int offset)
    {
        var lo = 0;
        var hi = lineStarts.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (lineStarts[mid] <= offset) lo = mid; else hi = mid - 1;
        }
        return lo + 1;
    }
}
