namespace DialogEditor.Patch.Diff;

public enum DiffExceptionKind { Unknown, NotARepo, BadRef, FileNotFound, ReadFailed }

/// Thrown when an endpoint cannot be loaded. Message is English (for logs); the UI
/// maps Kind to a localized string rather than showing Message directly.
public sealed class DiffException(string message, DiffExceptionKind kind = DiffExceptionKind.Unknown)
    : System.Exception(message)
{
    public DiffExceptionKind Kind { get; } = kind;
}
