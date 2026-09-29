using DialogEditor.Core.GameData;

namespace DialogEditor.Tests.Helpers;

/// A throwaway PoE1 install holding the canonical conversation (issue 122) in "en" and "de".
public sealed class FakePoe1Game : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"fakepoe1_{Guid.NewGuid():N}");
    public string DataRoot => Path.Combine(Root, "PillarsOfEternity_Data", "data");
    public string ConvPath() => Path.Combine(DataRoot, "conversations", "canonical", "canonical.conversation");
    public string StPath(string lang) =>
        Path.Combine(DataRoot, "localized", lang, "text", "conversations", "canonical", "canonical.stringtable");

    public FakePoe1Game() => CanonicalFixture.CopyTo("poe1", Root);

    public IGameDataProvider Provider => new Poe1GameDataProvider(Root);
    public ConversationFile File => Provider.EnumerateConversations().Single();

    public void Dispose() { try { Directory.Delete(Root, true); } catch (Exception) { /* best-effort */ } }
}
