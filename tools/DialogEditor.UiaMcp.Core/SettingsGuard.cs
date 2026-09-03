using System.Text.Json;
using System.Text.Json.Nodes;

namespace DialogEditor.UiaMcp.Core;

/// <summary>
/// Protects the user's real editor settings across an automation run.
///
/// Ported from DriveApp.ps1's settings lifecycle. Two rules are load-bearing and
/// both come from real incidents:
///   * the snapshot is a FILE, not a field — a run can span processes, and an
///     in-memory-only backup makes Restore a silent no-op in any later one;
///   * an existing backup is NEVER overwritten — if a previous run died before
///     restoring, the older file is the one holding the genuine settings.
/// </summary>
public sealed class SettingsGuard(string settingsPath, string backupPath)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Called at server startup. If a previous run died before restoring, put the
    /// user's settings back and describe what happened; otherwise return null.
    /// </summary>
    public string? RecoverStaleBackup()
    {
        if (!File.Exists(backupPath)) return null;
        var restored = Restore();
        return $"Recovered stale settings backup from a previous run. {restored}";
    }

    public void Backup()
    {
        if (!File.Exists(settingsPath)) return;
        if (File.Exists(backupPath)) return;   // older backup wins — see class remarks
        File.Copy(settingsPath, backupPath, overwrite: false);
    }

    /// <summary>Puts settings back. Returns a human-readable outcome for the tool result.</summary>
    public string Restore()
    {
        if (!File.Exists(backupPath))
            return "Restore skipped: no backup found — settings.json still holds whatever this run wrote.";

        File.Copy(backupPath, settingsPath, overwrite: true);
        File.Delete(backupPath);
        return "Settings restored from backup.";
    }

    public void SetLastProjectPath(string path)
    {
        if (!File.Exists(settingsPath)) return;
        var node = JsonNode.Parse(File.ReadAllText(settingsPath))
                   ?? throw new InvalidOperationException($"Unparseable settings file: {settingsPath}");
        node["LastProjectPath"] = path;
        File.WriteAllText(settingsPath, node.ToJsonString(Indented));
    }
}
