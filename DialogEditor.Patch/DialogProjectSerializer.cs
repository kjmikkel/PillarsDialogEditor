using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DialogEditor.Patch;

public static class DialogProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented              = true,
        PropertyNamingPolicy       = null,
        DefaultIgnoreCondition     = JsonIgnoreCondition.Never,
    };

    public static string Serialize(DialogProject project)
        => JsonSerializer.Serialize(project, Options);

    public static DialogProject Deserialize(string json)
        => JsonSerializer.Deserialize<DialogProject>(json, Options)
           ?? throw new InvalidOperationException("Deserialised project was null.");

    public static void SaveToFile(string path, DialogProject project)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Serialize(project), Encoding.UTF8);
    }

    public static DialogProject LoadFromFile(string path)
        => Deserialize(File.ReadAllText(path));
}
