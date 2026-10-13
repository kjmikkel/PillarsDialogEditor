namespace DialogEditor.Core.GameData;

/// <summary>A game folder the full backup keeps beyond the base conversations and text (PoE2 DLC
/// data folders, issue 198; PoE1 expansion text, issue 201). <see cref="SnapshotPath"/> is relative
/// to the backup folder. <see cref="SinceFormat"/> is the backup format that first kept it: an
/// older backup lacks it, and is topped up with it once.</summary>
public sealed record ExtraBackupFolder(string SnapshotPath, string LivePath, int SinceFormat = 3);
