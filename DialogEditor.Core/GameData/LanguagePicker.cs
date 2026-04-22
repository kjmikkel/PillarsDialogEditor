namespace DialogEditor.Core.GameData;

public static class LanguagePicker
{
    public static string Pick(IReadOnlyList<string> available, string? preferred)
    {
        if (!string.IsNullOrEmpty(preferred) && available.Contains(preferred))
            return preferred;
        if (available.Contains("en"))
            return "en";
        return available.Count > 0 ? available[0] : "en";
    }
}
