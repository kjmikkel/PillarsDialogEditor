namespace DialogEditor.Patch.Diff;

/// One side of a comparison: the on-disk working copy, or a git ref (branch/commit).
public abstract record DiffEndpoint
{
    public sealed record WorkingCopy : DiffEndpoint;
    public sealed record GitRef(string Ref) : DiffEndpoint;
}
