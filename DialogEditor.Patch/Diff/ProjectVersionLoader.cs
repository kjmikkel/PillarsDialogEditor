namespace DialogEditor.Patch.Diff;

public class ProjectVersionLoader(IGitRunner git)
{
    /// Loads the project for <paramref name="endpoint"/>. <paramref name="projectFilePath"/> is
    /// the working-copy path, used both to read the working copy and to locate the repo +
    /// relative path for refs.
    public DialogProject Load(DiffEndpoint endpoint, string projectFilePath)
    {
        var json = endpoint switch
        {
            DiffEndpoint.WorkingCopy   => ReadWorkingCopy(projectFilePath),
            DiffEndpoint.GitRef gitRef => ReadAtRef(gitRef.Ref, projectFilePath),
            _ => throw new DiffException("Unknown diff endpoint.", DiffExceptionKind.Unknown),
        };

        try { return DialogProjectSerializer.Deserialize(json); }
        catch (Exception ex) { throw new DiffException($"Could not read project: {ex.Message}", DiffExceptionKind.ReadFailed); }
    }

    private static string ReadWorkingCopy(string path)
    {
        if (!File.Exists(path))
            throw new DiffException($"Working-copy file not found: {path}", DiffExceptionKind.FileNotFound);

        try { return File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new DiffException($"Could not read working-copy file '{path}': {ex.Message}", DiffExceptionKind.ReadFailed);
        }
    }

    private string ReadAtRef(string gitRef, string projectFilePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(projectFilePath))
                  ?? throw new DiffException("Project path has no directory.", DiffExceptionKind.Unknown);

        var root = git.Run(dir, "rev-parse", "--show-toplevel");
        if (!root.Ok)
            throw new DiffException("Not a git repository (or git is not installed).", DiffExceptionKind.NotARepo);

        var repoRoot = root.StdOut.Trim();
        var relative = Path.GetRelativePath(repoRoot, Path.GetFullPath(projectFilePath))
                           .Replace('\\', '/');

        var show = git.Run(dir, "show", $"{gitRef}:{relative}");
        if (!show.Ok)
            throw new DiffException($"Could not read '{relative}' at '{gitRef}': {show.StdErr.Trim()}", DiffExceptionKind.BadRef);

        return show.StdOut;
    }
}
