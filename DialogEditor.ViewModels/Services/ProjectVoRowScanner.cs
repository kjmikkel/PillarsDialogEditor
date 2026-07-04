using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Builds batch-VO-import rows across every conversation the project patches
/// (design: docs/superpowers/specs/2026-07-04-batch-vo-all-conversations-design.md).
/// Mirrors VoOrphanScanner's walk: the conversation open on the canvas is
/// represented by its live snapshot so unsaved edits count; every other patched
/// conversation is loaded vanilla + patch (conflicts ignored — display
/// semantics); an unreadable conversation is skipped, never fatal.
/// </summary>
public static class ProjectVoRowScanner
{
    public static IReadOnlyList<BatchVoRowViewModel> BuildRows(
        DialogProject project,
        IGameDataProvider provider,
        string projectPath,
        string gameRoot,
        string activeGameId,
        string? openConversationName = null,
        ConversationEditSnapshot? openSnapshot = null)
    {
        var voRoot = VoPathResolver.VoicesRoot(gameRoot);
        var voDir  = Path.Combine(Path.GetDirectoryName(projectPath)!, "_vo");
        var rows   = new List<BatchVoRowViewModel>();

        foreach (var (convName, patch) in project.Patches)
        {
            ConversationEditSnapshot snap;
            if (convName == openConversationName && openSnapshot is not null)
            {
                snap = openSnapshot;
            }
            else
            {
                try
                {
                    var file     = provider.FindConversation(convName);
                    var baseSnap = file is not null
                        ? ConversationSnapshotBuilder.Build(provider.LoadConversation(file))
                        : new ConversationEditSnapshot([]);
                    snap = PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true);
                }
                catch (Exception ex)
                {
                    // Unreadable conversation: skip rather than fail the scan.
                    AppLog.Warn($"Batch VO scan: could not load '{convName}': {ex.Message}");
                    continue;
                }
            }

            // Added nodes carry no text in the snapshot ([JsonIgnore]) — their
            // text lives in the patch's translations (VoOrphanScanner precedent).
            var translations = (patch.Translations.GetValueOrDefault(provider.Language) ?? [])
                .ToDictionary(t => t.NodeId);

            foreach (var node in snap.Nodes)
            {
                // DefaultText/FemaleText are [JsonIgnore]: a patch deserialized
                // from disk materialises AddedNodes with NULL texts, not "".
                var femaleText = !string.IsNullOrEmpty(node.FemaleText) ? node.FemaleText
                    : translations.TryGetValue(node.NodeId, out var ft) ? ft.FemaleText
                    : string.Empty;

                var check = VoPathResolver.Check(
                    node.SpeakerGuid, node.HasVO, node.ExternalVO,
                    !string.IsNullOrEmpty(femaleText),
                    node.NodeId, convName, gameRoot, activeGameId);

                if (check is null || check.Status == VoPresence.NotApplicable) continue;
                if (check.PrimaryWemPath is null) continue;

                var rel         = Path.GetRelativePath(voRoot, check.PrimaryWemPath);
                var destPrimary = Path.Combine(voDir, rel);
                var destFem     = Path.Combine(voDir, rel[..^4] + "_fem.wem");

                var text = (node.DefaultText ?? string.Empty).Trim();
                if (text.Length == 0 && translations.TryGetValue(node.NodeId, out var tr))
                    text = (tr.DefaultText ?? string.Empty).Trim();
                var preview = text.Length == 0  ? Loc.Format("BatchVoImport_NodeFallback", node.NodeId)
                            : text.Length <= 60 ? text
                                                : text[..60] + "…";

                rows.Add(new BatchVoRowViewModel(
                    convName, node.NodeId, preview, check.Status, destPrimary, destFem,
                    isAliased: !string.IsNullOrEmpty(node.ExternalVO)));
            }
        }

        return rows
            .OrderBy(r => r.ConversationName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.NodeId)
            .ToList();
    }
}
