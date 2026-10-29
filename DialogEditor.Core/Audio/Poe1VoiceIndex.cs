using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using DialogEditor.Core.Logging;

namespace DialogEditor.Core.Audio;

/// A shipped PoE1 voice line: where its FSB5 bank sits in the game's resource file.
/// <paramref name="SourceFile"/> is a bare file name, resolved next to resources.assets.
public sealed record Poe1VoiceClip(string Name, string SourceFile, long Offset, long Size);

/// Name-to-clip index over PoE1's voice-over, built once from resources.assets (issue 4).
///
/// The game finds a line by the AudioClip name "conversation_node:0000" plus "_fem" for the
/// English female variant (GameResources.GetDialogueAudio). Only the record data is kept;
/// the Unity file is closed again before Load returns.
///
/// A name can occur in several records: placeholder stubs next to the real clip (the real one
/// wins), identical copies (harmless), or genuinely different clips under one name (261 on
/// the shipped game). The last kind is ambiguous and Find returns null for it: picking one
/// could play the wrong speaker's line. Resolving it needs the VOAsset-to-clip mapping, which
/// is follow-up work.
public sealed class Poe1VoiceIndex
{
    private const string DataFolder = "PillarsOfEternity_Data";
    private const string AssetsFile = "resources.assets";
    private const string SupportedUnityVersion = "2019.4.";
    private static readonly byte[] Fsb5Magic = "FSB5"u8.ToArray();
    private static readonly Regex VoName = new(@"_\d{4}(_fem)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Dictionary<string, (Poe1AudioClipRecord Record, Poe1VoiceClip Clip)> _clips =
        new(StringComparer.OrdinalIgnoreCase);

    /// For tests and Load: only VO-named records are indexed. A real clip is never replaced by
    /// a placeholder of the same name.
    internal Poe1VoiceIndex(IEnumerable<Poe1AudioClipRecord> records, string dataFolder)
    {
        foreach (var rec in records)
        {
            if (!VoName.IsMatch(rec.Name)) continue;
            if (_clips.TryGetValue(rec.Name, out var existing) && !existing.Record.IsPlaceholder)
            {
                if (!rec.IsPlaceholder && !SameData(existing.Record, rec)) _ambiguous.Add(rec.Name);
                continue;
            }
            _clips[rec.Name] = (rec, new Poe1VoiceClip(rec.Name, SourceFileName(rec.Source), rec.Offset, rec.Size));
        }
        _dataFolder = dataFolder;
    }

    private readonly string _dataFolder;
    private readonly HashSet<string> _ambiguous = new(StringComparer.OrdinalIgnoreCase);

    private static bool SameData(Poe1AudioClipRecord a, Poe1AudioClipRecord b) =>
        a.Offset == b.Offset && a.Size == b.Size
        && string.Equals(SourceFileName(a.Source), SourceFileName(b.Source), StringComparison.OrdinalIgnoreCase);

    /// Names with several different real clips; Find returns null for them.
    internal int AmbiguousCount => _ambiguous.Count;

    /// Number of voice-named clips, placeholders included.
    public int Count => _clips.Count;

    /// Null for a miss and for placeholder clips.
    public Poe1VoiceClip? Find(string conversationName, int nodeId, bool female)
    {
        var key = $"{conversationName}_{nodeId:0000}{(female ? "_fem" : "")}";
        if (_ambiguous.Contains(key)) return null;
        return _clips.TryGetValue(key, out var hit) && !hit.Record.IsPlaceholder ? hit.Clip : null;
    }

    /// Copies the clip's slice of the resource file to &lt;cacheDir&gt;/&lt;name&gt;.fsb and returns
    /// that path. An existing file of the right size is reused. Throws InvalidDataException when
    /// the slice runs past the file or is not an FSB5 bank.
    public string Extract(Poe1VoiceClip clip, string cacheDir)
    {
        if (Path.GetFileName(clip.Name) != clip.Name)
            throw new InvalidDataException($"Voice clip name '{clip.Name}' is not a plain file name.");

        var dest = Path.Combine(cacheDir, clip.Name + ".fsb");
        if (File.Exists(dest) && new FileInfo(dest).Length == clip.Size) return dest;

        var data = ReadSlice(Path.Combine(_dataFolder, clip.SourceFile), clip);
        Directory.CreateDirectory(cacheDir);
        var temp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, data);
            File.Move(temp, dest, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        return dest;
    }

    private static byte[] ReadSlice(string sourcePath, Poe1VoiceClip clip)
    {
        using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (clip.Offset < 0 || clip.Size < Fsb5Magic.Length || clip.Offset > fs.Length - clip.Size
            || clip.Size > int.MaxValue)
            throw new InvalidDataException(
                $"Voice clip '{clip.Name}' slice [{clip.Offset}, +{clip.Size}) does not fit in {clip.SourceFile} ({fs.Length} bytes).");

        fs.Position = clip.Offset;
        var data = new byte[clip.Size];
        fs.ReadExactly(data);
        if (!data.AsSpan(0, Fsb5Magic.Length).SequenceEqual(Fsb5Magic))
            throw new InvalidDataException($"Voice clip '{clip.Name}' is not an FSB5 bank.");
        return data;
    }

    /// The Source of a resource reference is a file name, possibly behind an "archive:/" path.
    private static string SourceFileName(string source)
    {
        var cut = source.LastIndexOfAny(['/', '\\']);
        return cut < 0 ? source : source[(cut + 1)..];
    }

    /// Opens the game's resources.assets and indexes its voice clips. Null (after a warning)
    /// when the file is missing, is not Unity 2019.4 (the AudioClip layout is version-specific),
    /// or cannot be read.
    public static Poe1VoiceIndex? Load(string gameRoot)
    {
        var dataFolder = Path.Combine(gameRoot, DataFolder);
        var assetsPath = Path.Combine(dataFolder, AssetsFile);
        if (!File.Exists(assetsPath))
        {
            AppLog.Warn($"PoE1 voice: {assetsPath} not found; no voice preview.");
            return null;
        }

        try
        {
            var manager = new AssetsManager();
            try
            {
                var inst = manager.LoadAssetsFile(assetsPath, false);
                var version = inst.file.Metadata.UnityVersion ?? "";
                if (!version.StartsWith(SupportedUnityVersion, StringComparison.Ordinal))
                {
                    AppLog.Warn($"PoE1 voice: unsupported Unity version '{version}' in {assetsPath}; no voice preview.");
                    return null;
                }

                var records = new List<Poe1AudioClipRecord>();
                var reader = inst.file.Reader;
                foreach (var info in inst.file.GetAssetsOfType(AssetClassID.AudioClip))
                {
                    reader.Position = info.GetAbsoluteByteOffset(inst.file);
                    records.Add(Poe1AudioClipRecord.Parse(reader));
                }
                var index = new Poe1VoiceIndex(records, dataFolder);
                AppLog.Info($"PoE1 voice: indexed {index.Count} voice clips; {index.AmbiguousCount} ambiguous names skipped.");
                return index;
            }
            finally
            {
                manager.UnloadAll();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"PoE1 voice: could not read {assetsPath}: {ex.Message}");
            return null;
        }
    }
}
