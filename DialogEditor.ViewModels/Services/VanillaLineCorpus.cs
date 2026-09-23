using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;

namespace DialogEditor.ViewModels.Services;

/// One line of the installed game's text. Text is the original, trimmed; IsFemale marks
/// the female variant.
public record VanillaLine(string ConversationName, int NodeId, string Text, bool IsFemale);

/// <summary>
/// Reads every conversation the game exposes, as it is on disk, for the duplicate sweep's
/// base-game comparison (issue #14). The only IO in that feature — everything downstream
/// (DuplicateLineScanner, QGramIndex) is pure.
///
/// Deliberately does NOT apply the project's patches (unlike SpeakerLineScanner): the
/// scanner removes the nodes whose text the project owns at scan time, so this list stays
/// valid while the project changes and can be cached for the window's lifetime.
///
/// The provider loads in its current Language, which is the project's primary language.
/// Female text is always emitted; the scan decides whether to use it, so toggling "Female
/// text" never forces a reload. Lines under the scanner's word floor are dropped here to
/// keep the index small. An unreadable conversation is warned and skipped, never fatal.
/// Spec: docs/superpowers/specs/2026-09-22-cross-vanilla-duplicate-detection-design.md
/// </summary>
public static class VanillaLineCorpus
{
    public static IReadOnlyList<VanillaLine> Load(IGameDataProvider provider, CancellationToken ct = default)
    {
        var lines = new List<VanillaLine>();

        foreach (var file in provider.EnumerateConversations())
        {
            ct.ThrowIfCancellationRequested();

            Conversation conv;
            try
            {
                conv = provider.LoadConversation(file);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Base-game duplicate scan: could not load '{file.Name}': {ex.Message}");
                continue;
            }

            foreach (var node in conv.Nodes)
            {
                var entry = conv.Strings.Get(node.NodeId);
                if (entry is null) continue;
                Add(conv.Name, node.NodeId, entry.DefaultText, female: false);
                Add(conv.Name, node.NodeId, entry.FemaleText,  female: true);
            }
        }

        return lines;

        void Add(string conv, int nodeId, string? text, bool female)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (DuplicateLineScanner.WordCount(DuplicateLineScanner.Normalize(text)) < DuplicateLineScanner.MinWords)
                return;
            lines.Add(new VanillaLine(conv, nodeId, text.Trim(), female));
        }
    }
}
