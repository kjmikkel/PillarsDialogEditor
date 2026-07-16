namespace DialogEditor.ViewModels;

/// Reading-time formatting for path stats: words ÷ 200 wpm, shown m:ss.
public static class PathStatsFormat
{
    private const int WordsPerMinute = 200;

    public static string ReadingTime(int words)
    {
        var totalSeconds = (int)Math.Round(words / (double)WordsPerMinute * 60);
        return $"{totalSeconds / 60}:{totalSeconds % 60:D2}";
    }
}
