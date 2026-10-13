using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.GameData;

public interface IGameDataProvider
{
    string GameName { get; }
    string GameId   { get; }   // "poe1" or "poe2" — used to filter the condition catalogue
    IReadOnlyList<string> AvailableLanguages { get; }
    string Language { get; set; }
    IReadOnlyList<ConversationFile> EnumerateConversations();
    Conversation LoadConversation(ConversationFile file);

    ConversationFile? FindConversation(string name) =>
        EnumerateConversations().FirstOrDefault(f => f.Name == name);
    IReadOnlyDictionary<string, string> LoadSpeakerNames();

    /// Returns a GUID → ChatterPrefix map for PoE2. Default returns empty; PoE1 unaffected.
    IReadOnlyDictionary<string, string> LoadChatterPrefixes() => new Dictionary<string, string>();

    /// Returns named entries grouped by lookup kind (e.g. "Quest", "Item", "GlobalVariable").
    /// Called once when a game folder is opened. Returns empty dict when no data is available.
    /// Default returns empty; override in providers that supply game data.
    IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>> LoadGameDataNames()
        => new Dictionary<string, IReadOnlyList<GameDataEntry>>();
    void   SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot);
    string GetStringTablePath(ConversationFile file);
    string GetStringTablePath(ConversationFile file, string language);
    (string ConversationsRoot, string StringTablesRoot) GetBackupRoots();

    /// The folder holding every conversation stringtable for <paramref name="language"/>,
    /// whatever <see cref="Language"/> is selected — what the full backup snapshots per
    /// language (issue 123). Test doubles that never back up a game folder needn't implement it.
    string GetStringTablesRoot(string language) => throw new NotSupportedException();

    /// Further folders the full backup keeps: PoE2's DLC data folders (issue 198). Empty for
    /// PoE1, whose DLC sits inside the base folders.
    IReadOnlyList<ExtraBackupFolder> GetExtraBackupFolders() => [];

    /// Returns a ConversationFile record for a not-yet-existing conversation,
    /// using this game's path conventions. Does not create any files.
    ConversationFile BuildNewConversationFile(string name);

    /// Writes a minimal blank template file to disk so that SaveConversation
    /// can subsequently apply a patch onto it. Creates parent directories.
    void InitializeConversationFile(ConversationFile file);

    /// Where a conversation created in the editor is registered with the game, or null when
    /// the game finds conversations by path and needs no registration (PoE1). Deadfire
    /// resolves names only through design/manifests/*conversations.manifest (issue 162).
    string? ConversationManifestPath(string name) => null;

    /// Writes the registration at <see cref="ConversationManifestPath"/>. Does nothing when
    /// that is null.
    void WriteConversationManifest(string name) { }
}
