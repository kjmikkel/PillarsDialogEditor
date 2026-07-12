using WeCantSpell.Hunspell;

namespace DialogEditor.ViewModels.Services;

/// The spell checker's three dictionary layers (see
/// docs/superpowers/specs/2026-07-11-spell-checker-design.md):
///  1. user-supplied Hunspell .aff/.dic pairs in the dictionaries folder
///     (filename prefix -> language code, "de_DE" -> "de"; affix-aware);
///  2. the generated game lexicon per language (embedded; case-tolerant set);
///  3. the user's personal word list (persisted; case-tolerant set).
/// A word is correct if ANY layer accepts it. All IO failures degrade to
/// "dictionary absent" with an AppLog warning — never a crash.
public sealed class SpellDictionaryStore
{
    private readonly string _dictionariesDirectory;
    private readonly string _userDictionaryPath;
    private readonly Dictionary<string, (string Dic, string Aff)> _pairs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WordList?> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _lexicons = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? _userWords;

    public SpellDictionaryStore(string dictionariesDirectory, string userDictionaryPath)
    {
        _dictionariesDirectory = dictionariesDirectory;
        _userDictionaryPath    = userDictionaryPath;
        Discover();
    }

    // ── App-default instance (app-data paths). Not used by tests. ──────────
    private static SpellDictionaryStore? _default;

    public static SpellDictionaryStore Default => _default ??= new SpellDictionaryStore(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PillarsDialogEditor", "dictionaries"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PillarsDialogEditor", "user-dictionary.txt"));

    public string DictionariesDirectory => _dictionariesDirectory;

    public IReadOnlyList<string> AvailableLanguages =>
        _pairs.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    public bool HasDictionary(string lang) =>
        WordListFor(lang) is not null;

    /// Layer 1 (Hunspell) OR layer 2 (lexicon) OR layer 3 (user words).
    public bool IsCorrect(string word, string lang)
    {
        if (SetContains(_lexicons.GetValueOrDefault(lang), word)) return true;
        if (SetContains(UserWords, word)) return true;
        var list = WordListFor(lang);
        return list is not null && list.Check(word);
    }

    /// Hunspell's top suggestion for a misspelled word, or null.
    public string? Suggest(string word, string lang)
    {
        var list = WordListFor(lang);
        if (list is null) return null;
        try
        {
            return list.Suggest(word).FirstOrDefault();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Spell suggestion failed for '{word}' ({lang}): {ex.Message}");
            return null;
        }
    }

    /// Layer 2: register the game lexicon for a language (embedded resources in
    /// production via EmbeddedLexicons.LoadInto; direct in tests).
    public void RegisterLexicon(string lang, IEnumerable<string> words)
        => _lexicons[lang] = new HashSet<string>(words, StringComparer.Ordinal);

    /// Layer 3: add a word to the personal dictionary and persist it.
    public void AddWord(string word)
    {
        word = word.Trim();
        if (word.Length == 0) return;
        var words = UserWords;
        if (!words.Add(word)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_userDictionaryPath)!);
            File.AppendAllLines(_userDictionaryPath, [word]);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to persist user dictionary word '{word}': {ex.Message}");
        }
    }

    // ── internals ───────────────────────────────────────────────────────────

    private void Discover()
    {
        try
        {
            if (!Directory.Exists(_dictionariesDirectory)) return;
            foreach (var dic in Directory.EnumerateFiles(_dictionariesDirectory, "*.dic"))
            {
                var aff = Path.ChangeExtension(dic, ".aff");
                if (!File.Exists(aff)) continue;
                var prefix = Path.GetFileNameWithoutExtension(dic);
                var lang   = prefix.Split('_', '-')[0].ToLowerInvariant();
                _pairs[lang] = (dic, aff);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Dictionary discovery failed in '{_dictionariesDirectory}': {ex.Message}");
        }
    }

    private WordList? WordListFor(string lang)
    {
        if (_loaded.TryGetValue(lang, out var cached)) return cached;
        WordList? list = null;
        if (_pairs.TryGetValue(lang, out var pair))
        {
            try
            {
                list = WordList.CreateFromFiles(pair.Dic, pair.Aff);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Failed to load dictionary '{pair.Dic}': {ex.Message}");
            }
        }
        _loaded[lang] = list;
        return list;
    }

    private HashSet<string> UserWords
    {
        get
        {
            if (_userWords is not null) return _userWords;
            _userWords = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(_userDictionaryPath))
                    foreach (var line in File.ReadAllLines(_userDictionaryPath))
                        if (line.Trim() is { Length: > 0 } w)
                            _userWords.Add(w);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Failed to load user dictionary '{_userDictionaryPath}': {ex.Message}");
            }
            return _userWords;
        }
    }

    private static bool SetContains(HashSet<string>? set, string word)
        => set is not null &&
           (set.Contains(word) || set.Contains(word.ToLowerInvariant()));
}

/// Loads the embedded per-language game lexicons (layer 2, generated by
/// tools/lexicon-gen) into a store. Resource shape: Resources/Lexicons/<lang>.txt,
/// one "word<TAB>count" per line — counts are curation metadata, ignored here.
public static class EmbeddedLexicons
{
    public static void LoadInto(SpellDictionaryStore store)
    {
        try
        {
            var assembly = typeof(EmbeddedLexicons).Assembly;
            foreach (var name in assembly.GetManifestResourceNames())
            {
                var marker = ".Resources.Lexicons.";
                var idx = name.IndexOf(marker, StringComparison.Ordinal);
                if (idx < 0 || !name.EndsWith(".txt", StringComparison.Ordinal)) continue;
                var lang = name[(idx + marker.Length)..^".txt".Length];

                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is null) continue;
                using var reader = new StreamReader(stream);
                var words = new List<string>();
                while (reader.ReadLine() is { } line)
                {
                    var tab = line.IndexOf('\t');
                    var word = (tab >= 0 ? line[..tab] : line).Trim();
                    if (word.Length > 0) words.Add(word);
                }
                store.RegisterLexicon(lang, words);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to load embedded lexicons: {ex.Message}");
        }
    }
}
