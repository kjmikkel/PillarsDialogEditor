using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.CanonicalMod;

/// The game files as the editor's own parser reads them back, per conversation and language.
public sealed class ModView
{
    private readonly Dictionary<string, IReadOnlyList<ConversationNode>> _nodes = new();
    private readonly Dictionary<(string, string), StringTable> _text = new();

    public static ModView FromGame(IGameDataProvider provider, IEnumerable<string> conversations, IEnumerable<string> languages)
    {
        var view  = new ModView();
        var langs = languages.ToList();
        foreach (var conv in conversations)
        {
            var file = provider.FindConversation(conv) ?? throw new InvalidOperationException($"'{conv}' not installed");
            foreach (var lang in langs)
            {
                provider.Language = lang;
                var c = provider.LoadConversation(file);
                view._nodes[conv]        = c.Nodes;
                view._text[(conv, lang)] = c.Strings;
            }
        }
        return view;
    }

    public bool Has(string conv, int id) => _nodes[conv].Any(n => n.NodeId == id);
    public ConversationNode Node(string conv, int id) => _nodes[conv].Single(n => n.NodeId == id);
    public StringEntry? Text(string conv, string lang, int id) => _text[(conv, lang)].Get(id);
}
