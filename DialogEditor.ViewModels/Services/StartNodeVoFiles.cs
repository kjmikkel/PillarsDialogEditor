using DialogEditor.Core.Logging;

namespace DialogEditor.ViewModels.Services;

/// The staged _vo/ files that follow a node renumbered to the start node (issue 172).
/// Default VO names carry the node ID (<conversation>_<id:0000>.wem), so a renumbered node
/// would otherwise lose its staged audio. A node with ExternalVO names its file explicitly,
/// so it has nothing to move.
internal sealed class StartNodeVoFiles
{
    private readonly IReadOnlyList<(string From, string To)> _moves;

    private StartNodeVoFiles(IReadOnlyList<(string From, string To)> moves) => _moves = moves;

    /// What is staged under <paramref name="fromId"/>'s name right now; null when nothing is.
    /// Worked out on every run, not once, because VO can be imported between an edit and its
    /// Undo or Redo.
    public static StartNodeVoFiles? For(string? projectPath, NodeViewModel node,
                                        string conversation, int fromId, int toId)
    {
        if (projectPath is null || !string.IsNullOrEmpty(node.ExternalVO)) return null;
        var oldRel = VoPathResolver.ExpectedRelativePath(node.SpeakerGuid, "", fromId, conversation);
        var newRel = VoPathResolver.ExpectedRelativePath(node.SpeakerGuid, "", toId, conversation);
        if (oldRel is null || newRel is null) return null;

        var voDir = Path.Combine(Path.GetDirectoryName(projectPath)!, "_vo");
        var moves = new List<(string, string)>();
        foreach (var suffix in new[] { ".wem", "_fem.wem" })
        {
            var from = Path.Combine(voDir, oldRel + suffix);
            if (File.Exists(from)) moves.Add((from, Path.Combine(voDir, newRel + suffix)));
        }
        return moves.Count == 0 ? null : new StartNodeVoFiles(moves);
    }

    public string? ExistingTarget => _moves.Select(m => m.To).FirstOrDefault(File.Exists);

    public void Forward() => Move(_moves);

    /// All or nothing: a failed move puts back the files already moved, then rethrows.
    private static void Move(IReadOnlyList<(string From, string To)> moves)
    {
        var done = new List<(string From, string To)>();
        try
        {
            foreach (var m in moves)
            {
                File.Move(m.From, m.To);
                done.Add(m);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Renaming staged voice-over for the start node failed", ex);
            foreach (var (from, to) in Enumerable.Reverse(done))
            {
                try { File.Move(to, from); }
                catch (Exception undoEx) { AppLog.Error($"Could not move {to} back to {from}", undoEx); }
            }
            throw;
        }
    }
}
