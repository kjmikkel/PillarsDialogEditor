using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

public record DialogProject(
    string Name,
    int SchemaVersion,
    IReadOnlyDictionary<string, ConversationPatch> Patches,
    // Canvas layout per conversation — metadata, not part of the patch diff.
    // Nullable so existing .dialogproject files without this field load cleanly.
    IReadOnlyDictionary<string, IReadOnlyDictionary<int, LayoutPoint>>? Layouts = null)
{
    public static readonly int CurrentSchemaVersion = 1;

    public static DialogProject Empty(string name) =>
        new(name, CurrentSchemaVersion, new Dictionary<string, ConversationPatch>());

    public DialogProject WithPatch(ConversationPatch patch) =>
        this with
        {
            Patches = new Dictionary<string, ConversationPatch>(Patches)
                { [patch.ConversationName] = patch }
        };

    public DialogProject WithLayout(
        string conversationName,
        IReadOnlyDictionary<int, LayoutPoint> positions) =>
        this with
        {
            Layouts = new Dictionary<string, IReadOnlyDictionary<int, LayoutPoint>>(
                Layouts ?? new Dictionary<string, IReadOnlyDictionary<int, LayoutPoint>>())
                { [conversationName] = positions }
        };

    public IReadOnlyDictionary<int, LayoutPoint>? GetLayout(string conversationName) =>
        Layouts?.GetValueOrDefault(conversationName);
}
