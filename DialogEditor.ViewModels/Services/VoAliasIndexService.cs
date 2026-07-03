using System.Text.Json;

namespace DialogEditor.ViewModels.Services;

/// <summary>One node that references a VO alias path. Conversation is the
/// file name without extension, lower-cased.</summary>
public record VoAliasRef(string Conversation, int NodeId);

/// <summary>
/// Session-wide reverse index: ExternalVO alias path → nodes referencing it.
/// Same lifecycle as SpeakerNameService: rebuilt per game-root open, in memory
/// only, so every app start re-reads current disk state (including newly
/// installed mods). Scans the base game AND override/*/design/conversations,
/// override winning per conversation file name — matching the game's own
/// precedence. Uses JsonDocument (not the full conversation parser): we only
/// need NodeID/ExternalVO pairs, and property order inside a node object is
/// not guaranteed, which rules out a flat regex scan.
/// </summary>
public static class VoAliasIndexService
{
    private static Dictionary<string, List<VoAliasRef>> _refs = new(StringComparer.OrdinalIgnoreCase);
    public static bool IsReady { get; private set; }

    public static void Rebuild(string gameRoot)
    {
        // Conversation file name (no extension, lower) → winning full path.
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void AddTree(string root)
        {
            if (!Directory.Exists(root)) return;
            foreach (var f in Directory.EnumerateFiles(root, "*.conversation*", SearchOption.AllDirectories))
                files[Path.GetFileNameWithoutExtension(f).ToLowerInvariant()] = f; // later wins
        }

        AddTree(Path.Combine(gameRoot, "PillarsOfEternityII_Data", "exported", "design", "conversations"));
        var overrideRoot = Path.Combine(gameRoot, "override");
        if (Directory.Exists(overrideRoot))
            foreach (var mod in Directory.EnumerateDirectories(overrideRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                AddTree(Path.Combine(mod, "design", "conversations"));

        var map = new Dictionary<string, List<VoAliasRef>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (convName, path) in files)
            ScanFile(map, convName, path);

        _refs   = map;
        IsReady = true;
    }

    private static void ScanFile(Dictionary<string, List<VoAliasRef>> map, string convName, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("Nodes", out var nodes)
                || nodes.ValueKind != JsonValueKind.Array)
                return;

            foreach (var node in nodes.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object) continue;
                if (!node.TryGetProperty("ExternalVO", out var ext)
                    || ext.ValueKind != JsonValueKind.String) continue;
                var alias = ext.GetString();
                if (string.IsNullOrEmpty(alias)) continue;
                if (!node.TryGetProperty("NodeID", out var idEl)
                    || !idEl.TryGetInt32(out var id)) continue;

                if (!map.TryGetValue(alias, out var list))
                    map[alias] = list = [];
                list.Add(new VoAliasRef(convName, id));
            }
        }
        catch (Exception ex)
        {
            // Unreadable/malformed conversation: skip rather than fail the index.
            AppLog.Warn($"VO alias index: could not scan '{path}': {ex.Message}");
        }
    }

    public static IReadOnlyList<VoAliasRef> GetReferences(string aliasPath)
        => _refs.TryGetValue(aliasPath, out var list) ? list : [];

    /// Test seam — mirrors SpeakerNameService.Register.
    public static void RegisterForTests(IReadOnlyDictionary<string, IReadOnlyList<VoAliasRef>> refs)
    {
        _refs   = refs.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.OrdinalIgnoreCase);
        IsReady = true;
    }

    public static void Clear()
    {
        _refs   = new Dictionary<string, List<VoAliasRef>>(StringComparer.OrdinalIgnoreCase);
        IsReady = false;
    }
}
