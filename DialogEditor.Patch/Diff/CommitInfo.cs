namespace DialogEditor.Patch.Diff;

/// One commit in a project's history. Presentation-free: Date is the parsed
/// author date; display formatting is the view layer's job.
public record CommitInfo(
    string         Sha,
    string         ShortSha,
    string         Author,
    DateTimeOffset Date,
    string         Subject)
{
    /// How long an abbreviated commit id is, in every window (GitHub issue 264): History and
    /// Compare Versions pass it to git as --abbrev, and blame cuts the full id to it, so one
    /// commit reads the same everywhere.
    public const int ShortLength = 8;
}
