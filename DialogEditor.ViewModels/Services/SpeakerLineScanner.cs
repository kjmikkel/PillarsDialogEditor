using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Whole-game read-only walk collecting every spoken line, per speaker, for the
/// Speaker Line Browser (voice-consistency checking).
/// Spec: docs/superpowers/specs/2026-07-13-speaker-line-browser-design.md
///
/// Unlike ProjectFindService / ProjectVoRowScanner (which walk only project.Patches),
/// this visits EVERY conversation the game exposes — the whole point is to compare the
/// writer's new lines against all the vanilla lines the character already speaks. The
/// open conversation is represented by its live snapshot so unsaved edits count; every
/// other conversation is loaded vanilla and (if the project patches it) has the patch
/// applied. New (not-on-disk) conversations the project adds are reached by unioning the
/// patch keys into the walk. An unreadable conversation is warned and skipped, never fatal.
/// The result is pure rows: name resolution / picker grouping is the ViewModel's job.
/// </summary>
public static class SpeakerLineScanner
{
    public static IReadOnlyList<SpeakerLineRow> Scan(
        DialogProject project,
        IGameDataProvider provider,
        string primaryLanguage,
        string? openConversationName = null,
        ConversationEditSnapshot? openSnapshot = null,
        CancellationToken ct = default)
    {
        var rows  = new List<SpeakerLineRow>();
        var files = provider.EnumerateConversations().ToDictionary(f => f.Name, StringComparer.Ordinal);

        // Union of on-disk conversations and patched names (the latter reaches new,
        // not-yet-on-disk conversations the project added).
        var names = files.Keys.Union(project.Patches.Keys, StringComparer.Ordinal);

        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();

            var patch = project.Patches.GetValueOrDefault(name);

            ConversationEditSnapshot snap;
            if (name == openConversationName && openSnapshot is not null)
            {
                snap = openSnapshot;
            }
            else
            {
                try
                {
                    var file     = files.GetValueOrDefault(name) ?? provider.FindConversation(name);
                    var baseSnap = file is not null
                        ? ConversationSnapshotBuilder.Build(provider.LoadConversation(file))
                        : new ConversationEditSnapshot([]);   // new conversation: patch supplies nodes
                    snap = patch is not null
                        ? PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true)
                        : baseSnap;
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"Speaker line scan: could not load '{name}': {ex.Message}");
                    continue;
                }
            }

            // Origin membership sets (empty when the conversation is unpatched → all Vanilla).
            var addedIds    = patch is null ? [] : patch.AddedNodes.Select(n => n.NodeId).ToHashSet();
            var modifiedIds = patch is null ? [] : patch.ModifiedNodes.Select(m => m.NodeId).ToHashSet();

            // Added nodes deserialized from disk carry [JsonIgnore] null text; fall back to
            // the primary-language translation entry (ProjectVoRowScanner precedent).
            var translations = (patch?.Translations.GetValueOrDefault(primaryLanguage) ?? [])
                .ToDictionary(t => t.NodeId);

            foreach (var node in snap.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.SpeakerGuid)) continue;   // unattributable

                var def = (!string.IsNullOrEmpty(node.DefaultText) ? node.DefaultText
                          : translations.TryGetValue(node.NodeId, out var pt) ? pt.DefaultText ?? "" : "").Trim();
                if (def.Length == 0) continue;   // no spoken line (Script/blank)

                var fem = (!string.IsNullOrEmpty(node.FemaleText) ? node.FemaleText
                          : translations.TryGetValue(node.NodeId, out var pf) ? pf.FemaleText ?? "" : "").Trim();

                var origin = addedIds.Contains(node.NodeId)    ? LineOrigin.New
                           : modifiedIds.Contains(node.NodeId) ? LineOrigin.Edited
                           : LineOrigin.Vanilla;

                rows.Add(new SpeakerLineRow(node.SpeakerGuid, name, node.NodeId, LineVariant.Default, def, origin));
                if (fem.Length > 0)
                    rows.Add(new SpeakerLineRow(node.SpeakerGuid, name, node.NodeId, LineVariant.Female, fem, origin));
            }
        }

        return rows
            .OrderBy(r => r.ConversationName, StringComparer.Ordinal)
            .ThenBy(r => r.NodeId)
            .ThenBy(r => r.Variant)
            .ToList();
    }
}
