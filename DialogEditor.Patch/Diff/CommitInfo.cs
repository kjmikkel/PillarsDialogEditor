namespace DialogEditor.Patch.Diff;

/// One commit in a project's history. Presentation-free: Date is the parsed
/// author date; display formatting is the view layer's job.
public record CommitInfo(
    string         Sha,
    string         ShortSha,
    string         Author,
    DateTimeOffset Date,
    string         Subject);
