namespace DialogEditor.Core.Audio;

/// Where Deadfire keeps its voice-over: the one definition of the path (issue 101).
///
/// There is one voice language and so one root. The game builds every conversation and
/// chatter VO path from a literal "English(US)" (ConversationAudioManager.GetConversationVOPath
/// and GetChatterVOPath), whatever the text language, and the shipped game installs only that
/// folder. A .wem staged under any other language folder would never be played.
public static class Poe2VoicePaths
{
    public static string VoicesRoot(string gameDir) => Path.Combine(gameDir,
        "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
}
