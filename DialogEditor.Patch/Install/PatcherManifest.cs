namespace DialogEditor.Patch.Install;

/// Overwritten: the file existed before the patcher first wrote it; its original bytes are kept
/// under files/. Created: the file did not exist, so restoring means deleting it.
public enum BackupEntryKind { Overwritten, Created }

/// One file the patcher manages. Path is relative to the game directory with '/' separators
/// so a manifest stays valid if the install folder is moved. LastWrittenSha256 is the hash of
/// what the patcher itself last wrote (null = not written since backup or restore); a file
/// matching neither hash was changed by someone else (a game update, the editor, another tool).
public sealed record BackupEntry(
    string          Path,
    BackupEntryKind Kind,
    string?         OriginalSha256,
    string?         LastWrittenSha256);

public sealed record PatcherManifest(
    int                        SchemaVersion,
    string                     PatcherVersion,
    IReadOnlyList<BackupEntry> Entries)
{
    public const int CurrentSchemaVersion = 1;
}
