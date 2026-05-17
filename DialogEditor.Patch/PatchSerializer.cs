using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DialogEditor.Patch;

public static class PatchSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = null,                    // preserve PascalCase
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize(ConversationPatch patch)
        => JsonSerializer.Serialize(patch, Options);

    public static ConversationPatch Deserialize(string json)
        => JsonSerializer.Deserialize<ConversationPatch>(json, Options)
           ?? throw new InvalidOperationException("Deserialised patch was null.");

    public static void SaveToFile(string path, ConversationPatch patch)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Serialize(patch), Encoding.UTF8);
    }

    public static ConversationPatch LoadFromFile(string path)
        => Deserialize(File.ReadAllText(path));
}
