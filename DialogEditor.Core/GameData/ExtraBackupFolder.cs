namespace DialogEditor.Core.GameData;

/// <summary>A game folder the full backup keeps beyond the base conversations and text (PoE2 DLC
/// data folders, issue 198). <see cref="SnapshotPath"/> is relative to the backup folder.</summary>
public sealed record ExtraBackupFolder(string SnapshotPath, string LivePath);
