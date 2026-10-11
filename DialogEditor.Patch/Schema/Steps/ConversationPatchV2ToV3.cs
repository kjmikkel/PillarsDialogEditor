using System.Text.Json.Nodes;

namespace DialogEditor.Patch.Schema.Steps;

/// ConversationPatch v3 (GitHub issue 139) renames a link's QuestionNodeTextDisplay values
/// "Always" → "ShowAlways" and "Never" → "ShowNever".
///
/// Why: both games declare OEIFormats' QuestionNodeDisplayType as ShowOnce / ShowAlways /
/// ShowNever, but up to v2 the link panel offered invented "Always" and "Never". PoE1 writes
/// the name straight into the conversation XML, and the game's XmlSerializer rejects any
/// name outside the enum, so one such link stopped the whole conversation from loading.
/// (PoE2 stores the enum as an integer and its serializer already mapped the old names, which
/// hid the mismatch.) From v3 the editor stores the game's own names, so a v2 patch holding
/// the old ones has to be brought forward or it would install an unloadable PoE1 conversation.
///
/// Links appear in three places: AddedNodes[].Links, ModifiedNodes[].AddedLinks and
/// ModifiedNodes[].ModifiedLinks. Only those values are touched, never a node's text or
/// comment that happens to read "Always". Every other value ("", "ShowOnce", the new names,
/// or anything unrecognised) is left as it is.
///
/// The names are spelled out here rather than taken from NodeEnumNames: a step describes one
/// historical change and must keep doing exactly that even if today's names change again.
public sealed class ConversationPatchV2ToV3 : ISchemaMigrationStep
{
    private const string DisplayKey = "QuestionNodeTextDisplay";

    private static readonly Dictionary<string, string> Renames = new()
    {
        ["Always"] = "ShowAlways",
        ["Never"]  = "ShowNever",
    };

    public SchemaFileKind Kind        => SchemaFileKind.ConversationPatch;
    public int            FromVersion => 2;

    public void Apply(JsonObject root)
    {
        foreach (var node in Objects(root["AddedNodes"]))
            RenameAll(node["Links"]);

        foreach (var node in Objects(root["ModifiedNodes"]))
        {
            RenameAll(node["AddedLinks"]);
            RenameAll(node["ModifiedLinks"]);
        }
    }

    private static void RenameAll(JsonNode? links)
    {
        foreach (var link in Objects(links))
            if (link[DisplayKey] is JsonValue value && value.TryGetValue<string>(out var old)
                && Renames.TryGetValue(old, out var renamed))
                link[DisplayKey] = renamed;
    }

    // A missing or malformed list is left for the deserializer to report, as with v1 → v2.
    private static IEnumerable<JsonObject> Objects(JsonNode? array) =>
        array is JsonArray items ? items.OfType<JsonObject>() : [];
}
