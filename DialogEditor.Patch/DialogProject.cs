using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

public record DialogProject(
    string Name,
    int SchemaVersion,
    IReadOnlyDictionary<string, ConversationPatch> Patches,
    // Canvas layout per conversation — metadata, not part of the patch diff.
    // Nullable so existing .dialogproject files without this field load cleanly.
    IReadOnlyDictionary<string, IReadOnlyDictionary<int, LayoutPoint>>? Layouts = null,
    // Names of conversations that don't yet exist on disk.
    // Nullable for back-compat with old .dialogproject files.
    IReadOnlyList<string>? NewConversations = null,
    // Canvas annotations per conversation — editor metadata only, never in game files.
    // Nullable for back-compat with old .dialogproject files.
    IReadOnlyDictionary<string, IReadOnlyList<AnnotationSnapshot>>? Annotations = null,
    // Duplicate-line ignore allowlist — editor metadata, never in game files.
    // Nullable for back-compat with projects saved before this field existed.
    IReadOnlyList<IgnoredDuplicate>? IgnoredDuplicates = null)
{
    public static readonly int CurrentSchemaVersion = 1;

    public static DialogProject Empty(string name) =>
        new(name, CurrentSchemaVersion, new Dictionary<string, ConversationPatch>());

    public DialogProject WithNewConversation(string name)
    {
        var existing = NewConversations ?? [];
        if (existing.Contains(name)) return this;
        return this with { NewConversations = [.. existing, name] };
    }

    public bool IsNewConversation(string name)
        => NewConversations?.Contains(name) == true;

    /// Merges another project into this one. Patches for the same conversation are
    /// combined using PatchMerger (the other project's values win on conflict).
    /// Layouts are merged with the other project winning on overlap. NewConversations
    /// lists are unioned.
    public DialogProject MergeWith(DialogProject other)
    {
        var allConversations = Patches.Keys
            .Concat(other.Patches.Keys)
            .Distinct();

        var mergedPatches = new Dictionary<string, ConversationPatch>();
        foreach (var name in allConversations)
        {
            var mine   = Patches.GetValueOrDefault(name);
            var theirs = other.Patches.GetValueOrDefault(name);
            mergedPatches[name] = (mine, theirs) switch
            {
                (not null, not null) => PatchMerger.Merge(name, [mine, theirs]),
                (not null, null)     => mine,
                _                    => theirs!,
            };
        }

        var result = this with { Patches = mergedPatches };

        foreach (var (convName, positions) in other.Layouts ?? new Dictionary<string, IReadOnlyDictionary<int, LayoutPoint>>())
            result = result.MergeLayout(convName, positions);

        var combined = (NewConversations ?? [])
            .Concat(other.NewConversations ?? [])
            .Distinct()
            .ToList();
        result = result with { NewConversations = combined.Count > 0 ? combined : null };

        var allAnnotationConvs = (Annotations?.Keys ?? [])
            .Concat(other.Annotations?.Keys ?? [])
            .Distinct();
        foreach (var convName in allAnnotationConvs)
        {
            var mine   = Annotations?.GetValueOrDefault(convName);
            var theirs = other.Annotations?.GetValueOrDefault(convName);
            if (mine is not null && theirs is not null)
                result = result.MergeAnnotations(convName, theirs);
            else if (mine is not null)
                result = result.WithAnnotations(convName, mine);
            else
                result = result.WithAnnotations(convName, theirs!);
        }

        return result;
    }

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

    public DialogProject WithAnnotations(
        string conversationName,
        IReadOnlyList<AnnotationSnapshot> annotations) =>
        this with
        {
            Annotations = new Dictionary<string, IReadOnlyList<AnnotationSnapshot>>(
                Annotations ?? new Dictionary<string, IReadOnlyList<AnnotationSnapshot>>())
                { [conversationName] = annotations }
        };

    public IReadOnlyList<AnnotationSnapshot>? GetAnnotations(string conversationName) =>
        Annotations?.GetValueOrDefault(conversationName);

    /// Adds a duplicate to the ignore allowlist (editor metadata). Deduped by
    /// Kind + Keys, so ignoring the same duplicate twice is a no-op.
    public DialogProject WithIgnoredDuplicate(IgnoredDuplicate entry)
    {
        var existing = IgnoredDuplicates ?? [];
        if (existing.Any(e => e.Kind == entry.Kind && e.Keys.SequenceEqual(entry.Keys)))
            return this;
        return this with { IgnoredDuplicates = [.. existing, entry] };
    }

    /// Removes a matching entry from the ignore allowlist; the list becomes null
    /// once empty (so a clean project serializes no ignore state).
    public DialogProject WithoutIgnoredDuplicate(IgnoredDuplicate entry)
    {
        if (IgnoredDuplicates is null) return this;
        var kept = IgnoredDuplicates
            .Where(e => !(e.Kind == entry.Kind && e.Keys.SequenceEqual(entry.Keys)))
            .ToList();
        return this with { IgnoredDuplicates = kept.Count > 0 ? kept : null };
    }

    /// Merges <paramref name="incoming"/> annotations into the existing set for
    /// <paramref name="conversationName"/>. Incoming wins on any ID collision;
    /// annotations present only in the existing set are preserved.
    public DialogProject MergeAnnotations(
        string conversationName,
        IReadOnlyList<AnnotationSnapshot> incoming)
    {
        var existing = Annotations?.GetValueOrDefault(conversationName)
                       ?? (IReadOnlyList<AnnotationSnapshot>)[];

        var merged = existing.ToDictionary(s => s.Id);
        foreach (var s in incoming)
            merged[s.Id] = s;

        return WithAnnotations(conversationName, [.. merged.Values]);
    }

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
