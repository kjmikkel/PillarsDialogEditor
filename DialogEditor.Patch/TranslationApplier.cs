using DialogEditor.Core.Logging;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Core.Serialization;

namespace DialogEditor.Patch;

public static class TranslationApplier
{
    /// <returns>The stringtables that were left untouched because they aren't readable XML
    /// (issue 119). A damaged file is never overwritten; the conversation and every other
    /// language are still written, as if that language weren't installed.</returns>
    public static IReadOnlyList<string> WriteTranslations(
        ConversationFile file,
        ConversationPatch patch,
        IGameDataProvider provider)
    {
        var unreadable = new List<string>();
        if (patch.Translations.Count == 0) return unreadable;
        var installed = provider.AvailableLanguages.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (lang, translations) in patch.Translations)
        {
            if (!installed.Contains(lang)) continue;
            var stPath = provider.GetStringTablePath(file, lang);
            Directory.CreateDirectory(Path.GetDirectoryName(stPath)!);
            try
            {
                StringTableSerializer.SaveToFile(stPath, translations);
            }
            catch (StringTableUnreadableException ex)
            {
                AppLog.Warn($"Skipped writing '{lang}' text for '{file.Name}': {ex.Message}");
                unreadable.Add(stPath);
            }
        }
        return unreadable;
    }

    /// The string-table paths WriteTranslations will write for this patch — the installer
    /// backs each one up before the write. Must stay in step with WriteTranslations' filter.
    public static IReadOnlyList<string> TargetPaths(
        ConversationFile file, ConversationPatch patch, IGameDataProvider provider)
    {
        var installed = provider.AvailableLanguages.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return patch.Translations.Keys
            .Where(installed.Contains)
            .Select(lang => provider.GetStringTablePath(file, lang))
            .ToList();
    }
}
