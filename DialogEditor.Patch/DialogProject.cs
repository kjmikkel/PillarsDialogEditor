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

    /// <summary>
    /// Merges <paramref name="incoming"/> positions into the existing layout for
    /// <paramref name="conversationName"/>. The incoming value wins for any node
    /// present in both; nodes present only in the existing layout are preserved.
    /// </summary>
    /// <remarks>
    /// Use this when combining two projects so that positions from both are kept.
    /// Contrast with <see cref="WithLayout"/>, which replaces the entire entry —
    /// the correct choice when saving from the canvas (where the caller supplies
    /// the complete current layout and stale deleted-node entries should be purged).
    /// </remarks>
    public DialogProject MergeLayout(
        string conversationName,
        IReadOnlyDictionary<int, LayoutPoint> incoming)
    {
        var allLayouts  = Layouts ?? new Dictionary<string, IReadOnlyDictionary<int, LayoutPoint>>();
        var existing    = allLayouts.GetValueOrDefault(conversationName)
                          ?? new Dictionary<int, LayoutPoint>();

        var merged = new Dictionary<int, LayoutPoint>(existing);
        foreach (var (id, pos) in incoming)
            merged[id] = pos;   // incoming wins on overlap

        return this with
        {
            Layouts = new Dictionary<string, IReadOnlyDictionary<int, LayoutPoint>>(allLayouts)
                { [conversationName] = merged }
        };
    }
}
