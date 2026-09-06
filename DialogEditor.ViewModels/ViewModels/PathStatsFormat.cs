namespace DialogEditor.ViewModels;

/// Reading-time formatting for path stats: words ÷ a reading speed, shown m:ss.
/// The speed is configurable (issue #14) — picked inline in the Flow Analytics window and
/// persisted as AppSettings.ReadingWordsPerMinute — because 200 wpm is a guess about a
/// reader who is not the writer: a VO director budgeting recording time and a writer
/// skimming a branch want different numbers, and every "longest playthrough" figure is
/// only as trustworthy as that constant.
public static class PathStatsFormat
{
    /// The historical speed, and the baseline every caller and test shares.
    public const int DefaultWordsPerMinute = 200;

    public static string ReadingTime(int words, int wordsPerMinute = DefaultWordsPerMinute)
    {
        // settings.json is hand-editable, so a zero or negative speed is reachable without
        // going through the UI's fixed preset list. Fall back rather than divide by zero.
        if (wordsPerMinute <= 0) wordsPerMinute = DefaultWordsPerMinute;

        var totalSeconds = (int)Math.Round(words / (double)wordsPerMinute * 60);
        return $"{totalSeconds / 60}:{totalSeconds % 60:D2}";
    }
}
