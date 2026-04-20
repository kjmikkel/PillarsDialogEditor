namespace DialogEditor.Core.Services;

public static class ConditionFormatter
{
    public static string Format(IReadOnlyList<string> conditions)
        => conditions.Count == 0 ? "None" : string.Join('\n', conditions);
}
