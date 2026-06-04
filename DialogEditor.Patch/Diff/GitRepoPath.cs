namespace DialogEditor.Patch.Diff;

/// Resolves a project file's location within its git repository. Shared by the
/// read-only git readers (ProjectVersionLoader, ProjectHistoryService).
public static class GitRepoPath
{
    /// Returns the directory to run git in (the project's folder) and the project's
    /// repo-root-relative, forward-slashed path. Throws DiffException(NotARepo) when
    /// the path is not inside a git repository (or git is unavailable).
    public static (string WorkingDir, string Relative) ResolveRepoRelative(
        IGitRunner git, string projectFilePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(projectFilePath))
                  ?? throw new DiffException("Project path has no directory.", DiffExceptionKind.Unknown);

        var root = git.Run(dir, "rev-parse", "--show-toplevel");
        if (!root.Ok)
            throw new DiffException("Not a git repository (or git is not installed).", DiffExceptionKind.NotARepo);

        var repoRoot = root.StdOut.Trim();
        var relative = Path.GetRelativePath(repoRoot, Path.GetFullPath(projectFilePath))
                           .Replace('\\', '/');
        return (dir, relative);
    }
}
