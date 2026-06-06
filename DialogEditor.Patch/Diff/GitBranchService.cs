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

    public BranchOpResult Checkout(string projectFilePath, string branch)
        => Guarded(projectFilePath, dir =>
        {
            var res = git.Run(dir, "checkout", branch);
            return res.Ok ? BranchOpResult.Success : ClassifyCheckoutFailure(dir, res);
        });

    // Runs `op` against the resolved repo dir, mapping a DiffException (not-a-repo /
    // git-missing) to a typed result instead of throwing. Mutating ops use this so the
    // VM can branch on Status; List() throws (the VM ctor catches it, like History).
    private BranchOpResult Guarded(string projectFilePath, Func<string, BranchOpResult> op)
    {
        try
        {
            var (dir, _) = GitRepoPath.ResolveRepoRelative(git, projectFilePath);
            return op(dir);
        }
        catch (DiffException ex)
        {
            return new BranchOpResult(ex.Kind switch
            {
                DiffExceptionKind.GitMissing => BranchOpStatus.GitMissing,
                DiffExceptionKind.NotARepo   => BranchOpStatus.NotARepo,
                _                            => BranchOpStatus.GitFailed,
            }, ex.Message);
        }
    }

    // Locale-safe: a failed checkout is classified by git status --porcelain, not by
    // parsing English stderr. Tracked modifications → offer commit; only untracked → case A.
    private BranchOpResult ClassifyCheckoutFailure(string dir, GitResult checkout)
    {
        var status = git.Run(dir, "status", "--porcelain");
        bool hasTracked = false, hasUntracked = false;
        foreach (var raw in status.StdOut.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (line.StartsWith("??")) hasUntracked = true;
            else                       hasTracked   = true;
        }
        if (hasTracked)   return new BranchOpResult(BranchOpStatus.BlockedByLocalChanges,  checkout.StdErr.Trim());
        if (hasUntracked) return new BranchOpResult(BranchOpStatus.BlockedByUntrackedFiles, checkout.StdErr.Trim());
        return new BranchOpResult(BranchOpStatus.GitFailed, checkout.StdErr.Trim());
    }

    public IReadOnlyList<string> ListUncommittedChanges(string projectFilePath)
    {
        var (dir, _) = GitRepoPath.ResolveRepoRelative(git, projectFilePath);
        var res = git.Run(dir, "status", "--porcelain");
        if (!res.Ok)
            throw new DiffException($"Could not read git status: {res.StdErr.Trim()}", DiffExceptionKind.Unknown);

        var files = new List<string>();
        foreach (var raw in res.StdOut.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 4) continue;       // porcelain entries are "XY <path>"
            if (line.StartsWith("??")) continue; // untracked excluded (tracked-only commit)
            // Rename lines look like "old -> new"; fine for a display list, not usable as a path.
            files.Add(line[3..].Trim());
        }
        return files;
    }

    public BranchOpResult Create(string projectFilePath, string newName)
        => Guarded(projectFilePath, dir =>
        {
            if (!IsValidName(dir, newName)) return new BranchOpResult(BranchOpStatus.NameInvalid);
            if (BranchExists(dir, newName)) return new BranchOpResult(BranchOpStatus.NameExists);
            var res = git.Run(dir, "checkout", "-b", newName);  // creates from HEAD AND switches; working tree unchanged
            return res.Ok ? BranchOpResult.Success : new BranchOpResult(BranchOpStatus.GitFailed, res.StdErr.Trim());
        });

    private bool IsValidName(string dir, string name)
        => git.Run(dir, "check-ref-format", "--branch", name).Ok;

    private bool BranchExists(string dir, string name)
        => git.Run(dir, "show-ref", "--verify", "--quiet", $"refs/heads/{name}").Ok;

    public BranchOpResult Rename(string projectFilePath, string? from, string to)
        => Guarded(projectFilePath, dir =>
        {
            if (!IsValidName(dir, to)) return new BranchOpResult(BranchOpStatus.NameInvalid);
            if (BranchExists(dir, to)) return new BranchOpResult(BranchOpStatus.NameExists);
            var res = from is null
                ? git.Run(dir, "branch", "-m", to)        // rename the current branch
                : git.Run(dir, "branch", "-m", from, to); // rename a named branch
            return res.Ok ? BranchOpResult.Success : new BranchOpResult(BranchOpStatus.GitFailed, res.StdErr.Trim());
        });

    public BranchOpResult Delete(string projectFilePath, string branch, bool force)
        => Guarded(projectFilePath, dir =>
        {
            var res = git.Run(dir, "branch", force ? "-D" : "-d", branch);
            if (res.Ok) return BranchOpResult.Success;
            // Safe (-d) refusal is almost always "not fully merged" → offer force.
            return force
                ? new BranchOpResult(BranchOpStatus.GitFailed, res.StdErr.Trim())
                : new BranchOpResult(BranchOpStatus.NotMerged, res.StdErr.Trim());
        });

    public BranchOpResult CommitAll(string projectFilePath, string message)
        => Guarded(projectFilePath, dir =>
        {
            var res = git.Run(dir, "commit", "-a", "-m", message);
            return res.Ok ? BranchOpResult.Success
                          : new BranchOpResult(BranchOpStatus.GitFailed, res.StdErr.Trim());
        });

    // null when detached (HEAD) or unreadable.
    private string? CurrentBranch(string dir)
    {
        var res  = git.Run(dir, "rev-parse", "--abbrev-ref", "HEAD");
        var name = res.StdOut.Trim();
        return res.Ok && name.Length > 0 && name != "HEAD" ? name : null;
    }
}
