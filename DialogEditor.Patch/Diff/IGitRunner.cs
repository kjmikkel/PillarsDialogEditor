namespace DialogEditor.Patch.Diff;

public record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// Abstraction over running git, so loaders are testable without a real repo.
public interface IGitRunner
{
    GitResult Run(string workingDirectory, params string[] args);
}
