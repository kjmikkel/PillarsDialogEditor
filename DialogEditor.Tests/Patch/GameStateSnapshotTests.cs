using DialogEditor.Core.Conditions;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

/// GitHub issue 344: the Game State panel's assumed state is saved with the project, so a
/// writer's test setup survives a restart. Editor metadata only, never in game files.
public class GameStateSnapshotTests
{
    private static GameState FullState()
    {
        var s = new GameState();
        s.Globals["n_Xoti_Affection"] = 3;
        s.Globals["N_xoti_affection"] = -1;   // globals are case-sensitive in both games
        s.CompanionsInParty["Companion_Eder"] = true;
        s.CompanionsInParty["Companion_Aloth"] = false;
        s.NoCompanionsInParty = true;
        s.PlayedNodes[("00_cv_xoti", 12)] = true;
        s.PlayedNodes[("00_cv_xoti", 14)] = false;
        s.Quests["01_quest_main"] = QuestStatus.Completed;
        s.Quests["02_quest_side"] = QuestStatus.Failed;
        s.InCombat = false;
        return s;
    }

    private static GameState RoundTrip(GameState s)
    {
        var p = DialogProject.Empty("P") with { GameState = GameStateSnapshot.From(s) };
        var back = DialogProjectSerializer.Deserialize(DialogProjectSerializer.Serialize(p));
        var restored = new GameState();
        GameStateSnapshot.ApplyTo(back.GameState, restored);
        return restored;
    }

    [Fact]
    public void Empty_HasNoGameState()
        => Assert.Null(DialogProject.Empty("P").GameState);

    [Fact]
    public void From_StateWithNoFacts_IsNull()
        => Assert.Null(GameStateSnapshot.From(new GameState()));

    [Fact]
    public void Serializer_RoundTripsEveryFact()
    {
        var back = RoundTrip(FullState());

        Assert.Equal(3, back.Globals["n_Xoti_Affection"]);
        Assert.Equal(-1, back.Globals["N_xoti_affection"]);
        Assert.True(back.CompanionsInParty["companion_eder"]);   // companion keys stay case-insensitive
        Assert.False(back.CompanionsInParty["Companion_Aloth"]);
        Assert.True(back.NoCompanionsInParty);
        Assert.True(back.PlayedNodes[("00_CV_XOTI", 12)]);       // played-node conversation too
        Assert.False(back.PlayedNodes[("00_cv_xoti", 14)]);
        Assert.Equal(QuestStatus.Completed, back.Quests["01_quest_main"]);
        Assert.Equal(QuestStatus.Failed, back.Quests["02_quest_side"]);
        Assert.False(back.InCombat);
    }

    [Fact]
    public void Serializer_WritesQuestStatusByName()
    {
        // Names, not enum ordinals: a reordered or extended QuestStatus must not misread old files.
        var s = new GameState();
        s.Quests["q"] = QuestStatus.Active;
        var json = DialogProjectSerializer.Serialize(DialogProject.Empty("P") with { GameState = GameStateSnapshot.From(s) });
        Assert.Contains("\"Active\"", json);
    }

    [Fact]
    public void ApplyTo_ReplacesWhatWasThere_AndNotifies()
    {
        var target = FullState();
        var notified = 0;
        target.Changed += (_, _) => notified++;

        var only = new GameState();
        only.Globals["g"] = 1;
        GameStateSnapshot.ApplyTo(GameStateSnapshot.From(only), target);

        Assert.Equal(new Dictionary<string, int> { ["g"] = 1 }, target.Globals);
        Assert.Empty(target.CompanionsInParty);
        Assert.False(target.NoCompanionsInParty);
        Assert.Empty(target.PlayedNodes);
        Assert.Empty(target.Quests);
        Assert.Null(target.InCombat);
        Assert.Equal(1, notified);
    }

    [Fact]
    public void ApplyTo_Null_ClearsTheState()
    {
        var target = FullState();
        GameStateSnapshot.ApplyTo(null, target);
        Assert.False(target.HasAnyFact);
    }

    [Fact]
    public void Deserialize_ProjectSavedBeforeTheField_LoadsWithNoGameState()
    {
        const string json = """{ "Name": "Old", "SchemaVersion": 1, "Patches": {} }""";
        Assert.Null(DialogProjectSerializer.Deserialize(json).GameState);
    }

    [Fact]
    public void Deserialize_UnknownQuestStatus_IsSkipped_NotThrown()
    {
        // A newer editor may add a status; this one leaves that quest unset rather than refusing the project.
        const string json = """
            { "Name": "P", "SchemaVersion": 1, "Patches": {},
              "GameState": { "Quests": { "a": "Completed", "b": "Abandoned" } } }
            """;
        var restored = new GameState();
        GameStateSnapshot.ApplyTo(DialogProjectSerializer.Deserialize(json).GameState, restored);
        Assert.Equal(QuestStatus.Completed, restored.Quests["a"]);
        Assert.False(restored.Quests.ContainsKey("b"));
    }

    [Fact]
    public void MergeWith_KeepsThisProjectsGameState()
    {
        var mine = new GameState(); mine.Globals["g"] = 1;
        var theirs = new GameState(); theirs.Globals["g"] = 2;
        var a = DialogProject.Empty("A") with { GameState = GameStateSnapshot.From(mine) };
        var b = DialogProject.Empty("B") with { GameState = GameStateSnapshot.From(theirs) };

        var restored = new GameState();
        GameStateSnapshot.ApplyTo(a.MergeWith(b).GameState, restored);
        Assert.Equal(1, restored.Globals["g"]);
    }
}
