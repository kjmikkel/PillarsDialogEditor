namespace DialogEditor.Core.Audio;

/// The game's route from a dialogue line to its AudioClip (issue 355): ResourceManager path
/// "audio/vocalization/vo assets/&lt;conversation&gt;/&lt;name&gt;" names a VOAsset, whose clip
/// string names (after normalisation) another container path holding the AudioClip. Unlike the
/// bare clip name, this tells apart two clips that share a name.
public static class Poe1VoiceRoutes
{
    private const string VoAssetFolder = "audio/vocalization/vo assets/";

    /// GlobalAudioPlayer.Coroutine_StreamClip's normalisation, exactly.
    public static string ResourcePathOf(string clip) =>
        Path.ChangeExtension(clip.ToLowerInvariant().Replace("data/", null), null);

    /// The container key (below the VO assets folder) for a node's line.
    public static string Key(string conversationName, int nodeId, bool female) =>
        $"{conversationName}/{conversationName}_{nodeId:0000}{(female ? "_fem" : "")}";

    /// Key to the PathId of the AudioClip in resources.assets. <paramref name="voAssetClipPath"/>
    /// reads a VOAsset's clip string by PathId and is only asked about VOAssets in that file.
    public static IReadOnlyDictionary<string, long> Build(
        IEnumerable<Poe1ResourceEntry> container, int resourcesFileId, Func<long, string?> voAssetClipPath)
    {
        var entries = container as IReadOnlyList<Poe1ResourceEntry> ?? container.ToList();
        var byPath = new Dictionary<string, Poe1ResourceEntry>(StringComparer.Ordinal);
        foreach (var e in entries) byPath.TryAdd(e.Path, e);

        var routes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (e.FileId != resourcesFileId || !e.Path.StartsWith(VoAssetFolder, StringComparison.Ordinal)) continue;
            var clip = voAssetClipPath(e.PathId);
            if (string.IsNullOrEmpty(clip)) continue;
            if (byPath.TryGetValue(ResourcePathOf(clip), out var target) && target.FileId == resourcesFileId)
                routes[e.Path[VoAssetFolder.Length..]] = target.PathId;
        }
        return routes;
    }
}
