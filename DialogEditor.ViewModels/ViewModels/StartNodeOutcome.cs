namespace DialogEditor.ViewModels;

/// What happened when the user asked to make a node the start node (issue 172).
public enum StartNodeOutcome { Made, NotAllowed, StartNodeExists, VoTargetExists, VoRenameFailed }

/// <param name="OldVoPath">The node's default VO path under its old ID, or null when it uses
/// ExternalVO; the main window counts aliases to it in other conversations.</param>
/// <param name="Detail">The existing file or the error message, for the status bar.</param>
public sealed record StartNodeChangedEventArgs(
    StartNodeOutcome Outcome, int OldNodeId, string? OldVoPath, string Detail = "");
