namespace DialogEditor.Core.Editing;

/// <summary>
/// Editor-only metadata for a canvas annotation box. Never written to game files.
/// Stored per-conversation in <see cref="DialogEditor.Patch.DialogProject.Annotations"/>,
/// alongside Layouts, using the same nullable back-compat pattern.
/// </summary>
public record AnnotationSnapshot(
    string Id,
    string Title,
    string Body,
    string ColorKey,
    double X,
    double Y,
    double Width,
    double Height);
