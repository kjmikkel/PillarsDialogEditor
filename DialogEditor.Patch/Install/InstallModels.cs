namespace DialogEditor.Patch.Install;

/// One load-order entry: a project, plus the extracted vo/ folder when it came from a .dialogpack.
public sealed record InstallEntry(DialogProject Project, string? VoFolder = null);

public sealed record InstallOptions(bool Force = false, bool AcceptCurrentFiles = false)
{
    /// Runs before every game-file write with the absolute path — a test seam for
    /// injecting a failure partway through an install.
    public Action<string>? BeforeWrite { get; init; }
}

public abstract record InstallResult
{
    private InstallResult() { }

    public sealed record Applied(
        int                   ConversationsPatched,
        IReadOnlyList<string> MissingConversations,
        IReadOnlyList<string> RestoreSkipped,
        int                   VoFilesCopied) : InstallResult
    {
        /// Stringtables left untouched because they aren't readable XML (issue 119).
        public IReadOnlyList<string> UnreadableStringTables { get; init; } = [];
    }

    /// Nothing was written: these managed files (relative paths) were changed outside the
    /// patcher, and the caller did not set AcceptCurrentFiles.
    public sealed record ExternalChanges(IReadOnlyList<string> Paths) : InstallResult;
}

public sealed record RestoreResult(int Restored, IReadOnlyList<string> Skipped);

public sealed record InstallPlan(
    int                   ConversationsToPatch,
    int                   FilesToRestoreFirst,
    IReadOnlyList<string> ExternallyChanged);
