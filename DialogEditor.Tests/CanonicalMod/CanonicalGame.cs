using DialogEditor.Core.GameData;

namespace DialogEditor.Tests.CanonicalMod;

/// One game's canonical mod (issue 138): its scenario and where its files live. The tests
/// take one of these instead of naming a game, so PoE1 and PoE2 share every check.
public sealed record CanonicalGame(
    string Id,
    IReadOnlyList<ScenarioStep> Steps,
    string Edited,
    string NewConversation,
    Func<string, IGameDataProvider> CreateProvider)
{
    public IReadOnlyList<string> Conversations => [Edited, NewConversation];
    public IReadOnlyList<string> Languages     => ["en", "de"];

    public IGameDataProvider Provider(string root) => CreateProvider(root);

    /// Where the editor puts the new conversation's file in an install: the F5 proof and
    /// the restore checks look there.
    public string NewConversationPath(string root) =>
        Provider(root).BuildNewConversationFile(NewConversation).ConversationPath;

    public string Pack    => Path.Combine(CanonicalModBuilder.GoldenDir(Id), CanonicalModBuilder.ProjectName + ".dialogpack");
    public string Project => Path.Combine(CanonicalModBuilder.GoldenDir(Id), CanonicalModBuilder.ProjectName + ".dialogproject");

    public static CanonicalGame Poe2 { get; } = new("poe2", Poe2Scenario.Steps, Poe2Scenario.Edited,
        Poe2Scenario.NewConversation, root => new Poe2GameDataProvider(root));

    public static CanonicalGame For(string id) => id switch
    {
        "poe2" => Poe2,
        _      => throw new ArgumentOutOfRangeException(nameof(id), id, null),
    };
}
