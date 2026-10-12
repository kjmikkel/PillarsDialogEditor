using System.Globalization;
using System.Text.Json;
using DialogEditor.Core.Analytics;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Logging;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

/// Counts references in the project's other conversations that still name a node's old ID
/// after it became the start node (issue 172). Counted for the status bar, never rewritten.
///
/// The editor has no script catalogue here, so the match is deliberately simple: a
/// conversation-jump call with one parameter naming this conversation (its name, a path
/// ending in it, or Deadfire's derived ID) and another equal to the old node ID.
public static class StartNodeReferenceScanner
{
    public static int Count(DialogProject project, string conversation, int oldId, string? oldVoPath)
    {
        var guid  = NewConversationId.For(conversation).ToString();
        var id    = oldId.ToString(CultureInfo.InvariantCulture);
        var vo    = Normalise(oldVoPath);
        var count = 0;

        foreach (var (name, patch) in project.Patches)
        {
            if (string.Equals(name, conversation, StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var node in patch.AddedNodes)
            {
                count += node.Scripts.Count(s => Names(s, conversation, guid, id));
                if (vo is not null && Normalise(node.ExternalVO) == vo) count++;
            }
            foreach (var mod in patch.ModifiedNodes)
            {
                count += (mod.UpdatedScripts ?? []).Count(s => Names(s, conversation, guid, id));
                if (vo is not null && mod.FieldChanges.TryGetValue("ExternalVO", out var change)
                    && Normalise(Decode(change.To)) == vo)
                    count++;
            }
        }
        return count;
    }

    private static bool Names(ScriptCall s, string conversation, string guid, string id) =>
        ConversationJumpVerbs.IsJump(s.DisplayName)
        && s.Parameters.Any(p =>
               string.Equals(Path.GetFileNameWithoutExtension(p.Replace('\\', '/')), conversation,
                             StringComparison.OrdinalIgnoreCase)
               || string.Equals(p, guid, StringComparison.OrdinalIgnoreCase))
        && s.Parameters.Contains(id);

    // FieldChange values are JSON-encoded; an unreadable one simply isn't a match.
    private static string? Decode(string json)
    {
        try { return JsonSerializer.Deserialize<string>(json); }
        catch (JsonException ex)
        {
            AppLog.Warn($"Start node reference scan: unreadable ExternalVO change: {ex.Message}");
            return null;
        }
    }

    private static string? Normalise(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.Replace('\\', '/').ToLowerInvariant();
        return p.EndsWith(".wem", StringComparison.Ordinal) ? p[..^4] : p;
    }
}
