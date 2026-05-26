using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class TranslationApplierTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly StubProvider _provider;

    public TranslationApplierTests()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "localized", "en"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "localized", "fr"));
        _provider = new StubProvider(_tempRoot, ["en", "fr"]);
    }

    public void Dispose() => Directory.Delete(_tempRoot, true);

    private static ConversationFile MakeFile(string name) =>
        new(name, string.Empty, name + ".conversation", string.Empty);

    [Fact]
    public void WriteTranslations_WritesEnAndFr_WhenBothInstalled()
    {
        var file  = MakeFile("intro");
        var patch = new ConversationPatch("intro", 2, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(1, "Hello", "")],
                ["fr"] = [new NodeTranslation(1, "Bonjour", "")],
            }
        };
        TranslationApplier.WriteTranslations(file, patch, _provider);

        var enPath = _provider.GetStringTablePath(file, "en");
        var frPath = _provider.GetStringTablePath(file, "fr");
        Assert.True(File.Exists(enPath));
        Assert.True(File.Exists(frPath));
        Assert.Contains("Hello",   File.ReadAllText(enPath));
        Assert.Contains("Bonjour", File.ReadAllText(frPath));
    }

    [Fact]
    public void WriteTranslations_SkipsUninstalledLanguage()
    {
        var file  = MakeFile("intro");
        var patch = new ConversationPatch("intro", 2, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["de"] = [new NodeTranslation(1, "Hallo", "")],
            }
        };
        TranslationApplier.WriteTranslations(file, patch, _provider);
        var dePath = _provider.GetStringTablePath(file, "de");
        Assert.False(File.Exists(dePath));
    }

    [Fact]
    public void WriteTranslations_EmptyTranslations_WritesNothing()
    {
        var file  = MakeFile("intro");
        var patch = new ConversationPatch("intro", 2, [], [], []);
        TranslationApplier.WriteTranslations(file, patch, _provider);
        Assert.Empty(Directory.GetFiles(_tempRoot, "*.stringtable", SearchOption.AllDirectories));
    }

    [Fact]
    public void WriteTranslations_AuthorLanguageTreatedLikeAny()
    {
        var file  = MakeFile("intro");
        var patch = new ConversationPatch("intro", 2, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(5, "Author text", "")],
            }
        };
        TranslationApplier.WriteTranslations(file, patch, _provider);
        var enPath = _provider.GetStringTablePath(file, "en");
        Assert.True(File.Exists(enPath));
        Assert.Contains("Author text", File.ReadAllText(enPath));
    }

    private sealed class StubProvider(string root, string[] languages) : IGameDataProvider
    {
        public string GameName => "Stub";
        public string GameId   => "stub";
        public string Language { get; set; } = "en";
        public IReadOnlyList<string> AvailableLanguages => languages;

        public string GetStringTablePath(ConversationFile file)
            => GetStringTablePath(file, Language);

        public string GetStringTablePath(ConversationFile file, string language)
            => Path.Combine(root, "localized", language, "text", "conversations",
                            file.Name + ".stringtable");

        public IReadOnlyList<ConversationFile> EnumerateConversations() => [];
        public Conversation LoadConversation(ConversationFile file) => throw new NotImplementedException();
        public IReadOnlyDictionary<string, string> LoadSpeakerNames() => new Dictionary<string, string>();
        public void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot) { }
        public (string, string) GetBackupRoots() => (root, root);
        public ConversationFile BuildNewConversationFile(string name) => throw new NotImplementedException();
        public void InitializeConversationFile(ConversationFile file) { }
    }
}
