using DialogEditor.Core.Localisation;

namespace DialogEditor.Patch.Install;

/// The manifest could not be read. Never "repaired" by recreating it: that would discard the
/// only record of which originals the backup holds.
[NotLocalised("Diagnostic message; the UI and CLI render ManifestPath with their own copy")]
public sealed class PatcherBackupCorruptException(string manifestPath, string reason, Exception? inner = null)
    : Exception($"Patcher backup manifest '{manifestPath}' is unreadable: {reason}", inner)
{
    public string ManifestPath { get; } = manifestPath;
}
