using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Finds .wem files in the project's _vo/ staging folder that no VO-enabled node
/// references (design: docs/superpowers/specs/2026-07-02-vo-lifecycle-design.md).
/// The expected set is computed project-wide: every patched conversation is loaded
/// (vanilla + patch, conflicts ignored — display semantics), and the conversation
/// open on the canvas is represented by its live snapshot so unsaved edits count.
/// A _fem.wem is only expected when the node has female text (intent-driven).
/// </summary>
public static class VoOrphanScanner
{
    public static IReadOnlyList<string> FindOrphans(
        DialogProject project,
        IGameDataProvider provider,
        string projectPath,
        string? openConversationName = null,
        ConversationEditSnapshot? openSnapshot = null)
    {
        var voDir = Path.Combine(Path.GetDirectoryName(projectPath)!, "_vo");
        if (!Directory.Exists(voDir)) return [];

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (convName, patch) in project.Patches)
        {
            ConversationEditSnapshot snap;
            IReadOnlyDictionary<int, string> femOverride;

            if (convName == openConversationName && openSnapshot is not null)
            {
                // Live canvas text is authoritative for the open conversation.
                snap        = openSnapshot;
                femOverride = new Dictionary<int, string>();
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
                    // Unreadable conversation: skip rather than flag its files.
                    AppLog.Warn($"Orphan scan: could not load '{convName}': {ex.Message}");
                    continue;
                }
                // Added nodes carry no text in the snapshot ([JsonIgnore]) — female
                // text for them lives in the patch's translations.
                femOverride = (patch.Translations.GetValueOrDefault(provider.Language) ?? [])
                    .ToDictionary(t => t.NodeId, t => t.FemaleText);
            }

            foreach (var node in snap.Nodes)
            {
                if (!node.HasVO && string.IsNullOrEmpty(node.ExternalVO)) continue;

                var relBase = VoPathResolver.ExpectedRelativePath(
                    node.SpeakerGuid, node.ExternalVO, node.NodeId, convName);
                if (relBase is null) continue;   // unknown speaker — claim nothing

                expected.Add(relBase + ".wem");
                var femText = femOverride.TryGetValue(node.NodeId, out var t)
                    ? t : node.FemaleText;
                if (!string.IsNullOrEmpty(femText))
                    expected.Add(relBase + "_fem.wem");
            }
        }

        return Directory.EnumerateFiles(voDir, "*.wem", SearchOption.AllDirectories)
            .Where(f => !expected.Contains(Path.GetRelativePath(voDir, f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
