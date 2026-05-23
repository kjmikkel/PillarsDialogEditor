using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DialogEditor.Core.Parsing;

public static partial class Poe1SpeakerNameParser
{
    // Strips optional expansion prefix (PX1_, PX2_, …) then category prefix
    [GeneratedRegex(@"^(?:PX\d+_)?(?:NPC|CRE|Companion|BES|LK|Bell|Door)_",
        RegexOptions.IgnoreCase)]
    private static partial Regex CategoryPrefix();

    // Strips a trailing whitespace+digits suffix used for numbered instances
    [GeneratedRegex(@"\s+\d+$")]
    private static partial Regex TrailingNumber();

    // Companions whose InstanceTag codenames bear no relation to their display names
    private static readonly Dictionary<string, string> CodeNameOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["GGP"] = "Durance",
            ["GM"]  = "Grieving Mother",
        };

    public static IReadOnlyDictionary<string, string> Parse(
        IEnumerable<string> conversationXmls,
        string charactersStringtableXml)
    {
        var guidToTag = ParseCharacterMappings(conversationXmls);
        var names    = ParseStringtable(charactersStringtableXml);
        return Resolve(guidToTag, names);
    }

    public static IReadOnlyDictionary<string, string> ParseFromDisk(
        string conversationsRoot,
        string charactersStringtablePath)
    {
        var convXmls = Directory.Exists(conversationsRoot)
            ? Directory.EnumerateFiles(conversationsRoot, "*.conversation", SearchOption.AllDirectories)
                       .Select(p => File.ReadAllText(p, Encoding.UTF8))
            : [];
        var stXml = File.Exists(charactersStringtablePath)
            ? File.ReadAllText(charactersStringtablePath, Encoding.UTF8)
            : "<StringTableFile><Entries/></StringTableFile>";
        return Parse(convXmls, stXml);
    }

    private static Dictionary<string, string> ParseCharacterMappings(IEnumerable<string> xmls)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var xml in xmls)
        {
            foreach (var mapping in XDocument.Parse(xml).Descendants("CharacterMapping"))
            {
                var guid = (string?)mapping.Element("Guid");
                var tag  = (string?)mapping.Element("InstanceTag");
                if (!string.IsNullOrEmpty(guid) && !string.IsNullOrEmpty(tag) && !result.ContainsKey(guid))
                    result[guid] = tag;
            }
        }
        return result;
    }

    private static List<string> ParseStringtable(string xml)
        => XDocument.Parse(xml)
                    .Descendants("Entry")
                    .Select(e => (string?)e.Element("DefaultText"))
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!)
                    .ToList();

    private static IReadOnlyDictionary<string, string> Resolve(
        Dictionary<string, string> guidToTag,
        List<string> names)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (guid, tag) in guidToTag)
            result[guid] = ResolveTag(tag, names);
        return result;
    }

    private static string ResolveTag(string tag, List<string> names)
    {
        var normalized = CategoryPrefix().Replace(tag, "").Replace('_', ' ').Trim();

        if (CodeNameOverrides.TryGetValue(normalized, out var overrideName)) return overrideName;

        var match = names.FirstOrDefault(n => string.Equals(n, normalized, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match;

        // Remove trailing number and try again ("Audience Member 3" → "Audience Member")
        var noSuffix = TrailingNumber().Replace(normalized, "").Trim();
        if (noSuffix != normalized)
        {
            match = names.FirstOrDefault(n => string.Equals(n, noSuffix, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return normalized;
    }
}
