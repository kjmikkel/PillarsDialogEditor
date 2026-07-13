using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.ViewModels.Services;

/// Rebuilds a DialogProject with the given stale rows removed. Comments and
/// translations live on the ConversationPatch; layout lives on the project.
/// Pure: returns a new immutable project, mutates nothing.
public static class StaleDataPruner
{
    public static DialogProject Prune(DialogProject project, IReadOnlyList<StaleDataRow> rows)
    {
        var result = project;

        foreach (var group in rows.GroupBy(r => r.ConversationName))
        {
            var conv = group.Key;

            if (result.Patches.TryGetValue(conv, out var patch))
            {
                var commentIds = group.Where(r => r.Kind == StaleDataKind.Comment)
                                      .Select(r => r.NodeId).ToHashSet();
                var transKeys  = group.Where(r => r.Kind == StaleDataKind.Translation)
                                      .Select(r => (r.NodeId, r.Language)).ToHashSet();

                var newComments = commentIds.Count == 0
                    ? patch.NodeComments
                    : patch.NodeComments.Where(kv => !commentIds.Contains(kv.Key))
                                        .ToDictionary(kv => kv.Key, kv => kv.Value);

                var newTranslations = new Dictionary<string, IReadOnlyList<NodeTranslation>>();
                foreach (var (lang, entries) in patch.Translations)
                {
                    var kept = entries.Where(t => !transKeys.Contains((t.NodeId, lang))).ToList();
                    if (kept.Count > 0) newTranslations[lang] = kept;
                }

                result = result.WithPatch(patch with
                {
                    NodeComments = newComments,
                    Translations = newTranslations,
                });
            }

            var layoutIds = group.Where(r => r.Kind == StaleDataKind.Layout)
                                 .Select(r => r.NodeId).ToHashSet();
            if (layoutIds.Count > 0 && result.GetLayout(conv) is { } layout)
            {
                result = result.WithLayout(conv,
                    layout.Where(kv => !layoutIds.Contains(kv.Key))
                          .ToDictionary(kv => kv.Key, kv => kv.Value));
            }
        }

        return result;
    }
}
