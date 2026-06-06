namespace DialogEditor.Patch.Diff;

/// One local branch. Presentation-free.
public record BranchInfo(string Name, bool IsCurrent);

public enum BranchOpStatus
{
    Ok,
    BlockedByLocalChanges,    // tracked modifications block checkout → offer commit-all
    BlockedByUntrackedFiles,  // untracked files would be overwritten → cannot auto-fix (case A)
    NotMerged,                // safe delete refused → offer force-delete
    NameInvalid,              // create/rename: name fails git ref-format rules
    NameExists,               // create/rename: a branch with that name already exists
    GitMissing,               // git executable not installed / not on PATH
    NotARepo,                 // git present, but the project is not inside a git repo
    GitFailed                 // any other non-zero exit; Detail carries stderr for the log
}

public record BranchOpResult(BranchOpStatus Status, string? Detail = null)
{
    public static readonly BranchOpResult Success = new(BranchOpStatus.Ok);
}

/// Local-branch operations over a project file's git repo. Returns typed results
/// (never raw git text); the VM maps Status to localized copy. Testable via IGitRunner.
public class GitBranchService(IGitRunner git)
{
    public IReadOnlyList<BranchInfo> List(string projectFilePath)
    {
        var (dir, _) = GitRepoPath.ResolveRepoRelative(git, projectFilePath);
        var current  = CurrentBranch(dir);

        var res = git.Run(dir, "for-each-ref", "refs/heads", "--format=%(refname:short)");
        if (!res.Ok)
            throw new DiffException($"Could not list branches: {res.StdErr.Trim()}", DiffExceptionKind.Unknown);

        var branches = new List<BranchInfo>();
        foreach (var raw in res.StdOut.Split('\n'))
        {
            var name = raw.Trim();
            if (name.Length == 0) continue;
            branches.Add(new BranchInfo(name, IsCurrent: name == current));
        }
        return branches;
    }

    // null when detached (HEAD) or unreadable.
    private string? CurrentBranch(string dir)
    {
        var res  = git.Run(dir, "rev-parse", "--abbrev-ref", "HEAD");
        var name = res.StdOut.Trim();
        return res.Ok && name.Length > 0 && name != "HEAD" ? name : null;
    }
}
