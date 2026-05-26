using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Core.Serialization;

namespace DialogEditor.Patch;

public static class TranslationApplier
{
    public static void WriteTranslations(
        ConversationFile file,
        ConversationPatch patch,
        IGameDataProvider provider)
    {
        if (patch.Translations.Count == 0) return;
        var installed = provider.AvailableLanguages.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (lang, translations) in patch.Translations)
        {
            if (!installed.Contains(lang)) continue;
            var stPath = provider.GetStringTablePath(file, lang);
            Directory.CreateDirectory(Path.GetDirectoryName(stPath)!);
            StringTableSerializer.SaveToFile(stPath, translations);
        }
    }
}
