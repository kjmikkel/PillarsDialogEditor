using System.Text.Json;
using DialogEditor.Core.Localisation;
using DialogEditor.Core.Logging;

namespace DialogEditor.Core.Backup;

/// <summary>
/// What the editor has written into a game folder, kept next to that folder's full backup
/// (issue 118) so Restore Full Backup can tell the editor's writes from a game update.
/// </summary>
/// <remarks>
/// Test Patch (F5) appends one entry per file it changes. The journal is never trimmed by
/// Restore Conversation (F6): if F6's own bookkeeping is ever lost (a crash, a reset
/// settings file), this is what still identifies the editor's writes. Losing or corrupting
/// the journal is safe in one direction only — Restore Full Backup then skips more files,
/// it never overwrites more.
/// </remarks>
[NotLocalised("File-format and path handling only")]
public static class EditorWriteJournal
{
    public const string FileName = "editor-writes.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static IReadOnlyList<EditorWrite> Load(string journalPath)
    {
        if (!File.Exists(journalPath)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<EditorWrite>>(File.ReadAllText(journalPath)) ?? [];
        }
        catch (JsonException ex)
        {
            AppLog.Warn($"Editor write journal '{journalPath}' is unreadable and was ignored: {ex.Message}");
            return [];
        }
    }

    public static void Append(string journalPath, IEnumerable<EditorWrite> writes)
    {
        var all = Load(journalPath).ToList();
        foreach (var w in writes)
            if (!all.Contains(w)) all.Add(w);

        Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);
        File.WriteAllText(journalPath, JsonSerializer.Serialize(all, JsonOptions));
    }
}
