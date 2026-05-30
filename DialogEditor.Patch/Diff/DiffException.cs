namespace DialogEditor.Patch.Diff;

/// Thrown when an endpoint cannot be loaded (git unavailable, bad ref, file not
/// tracked, unreadable project). Message is safe to show to the user.
public sealed class DiffException(string message) : System.Exception(message);
