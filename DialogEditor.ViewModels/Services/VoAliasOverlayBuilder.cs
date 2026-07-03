using System.Text.Json;
using DialogEditor.Patch;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Builds the in-memory ExternalVO overlay for the alias shared-count: the open
/// project's patched values (added + modified nodes) with the live canvas nodes
/// winning for the open conversation. An entry with an empty AliasPath still
/// matters — it shadows a disk-index reference that the session has removed.
///
/// Why an overlay instead of re-reading the disk VO alias index directly: the
/// shared-count needs to reflect *unsaved* session edits (a node whose
/// ExternalVO the user just changed on the canvas, or added/modified nodes
/// sitting only in an unapplied ConversationPatch) so the count updates live
/// as the user types, not only after Save. The live canvas always wins over
/// the patch for the currently-open conversation because the patch may be
/// stale relative to in-progress edits that haven't been diffed yet.
/// </summary>
public static class VoAliasOverlayBuilder
{
    public static IReadOnlyList<VoAliasUse> Build(
        IReadOnlyDictionary<string, ConversationPatch>? patches,
        string? openConversation,
        IEnumerable<(int NodeId, string ExternalVO)>? openNodes)
    {
        var uses = new Dictionary<(string Conv, int Id), string>();

        if (patches is not null)
            foreach (var (conv, patch) in patches)
            {
                foreach (var added in patch.AddedNodes)
                    uses[(conv, added.NodeId)] = added.ExternalVO;
                foreach (var mod in patch.ModifiedNodes)
                    if (mod.FieldChanges.TryGetValue("ExternalVO", out var fc))
                        uses[(conv, mod.NodeId)] =
                            JsonSerializer.Deserialize<string>(fc.To) ?? string.Empty;
            }

        if (openConversation is not null && openNodes is not null)
            foreach (var (id, ext) in openNodes)
                uses[(openConversation, id)] = ext;   // live canvas wins

        return uses.Select(kv => new VoAliasUse(kv.Key.Conv, kv.Key.Id, kv.Value)).ToList();
    }
}
