namespace DialogEditor.Patch;

public record DialogProject(
    string Name,
    int SchemaVersion,
    IReadOnlyDictionary<string, ConversationPatch> Patches)
{
    public static readonly int CurrentSchemaVersion = 1;

    public static DialogProject Empty(string name) =>
        new(name, CurrentSchemaVersion, new Dictionary<string, ConversationPatch>());

    public DialogProject WithPatch(ConversationPatch patch) =>
        this with
        {
            Patches = new Dictionary<string, ConversationPatch>(Patches)
                { [patch.ConversationName] = patch }
        };
}
