using DialogEditor.Core.Resources;

namespace DialogEditor.Core.Parsing;

public static class ConditionFormatter
{
    public static string FormatScript(string fullName, IReadOnlyList<string> parameters)
    {
        var funcName = ExtractFunctionName(fullName);
        var paramStr = string.Join(", ", parameters);
        return $"{funcName}({paramStr})";
    }

    public static string Format(string fullName, IReadOnlyList<string> parameters, bool not)
    {
        var funcName = ExtractFunctionName(fullName);
        var paramStr = string.Join(", ", parameters);
        var condition = $"{funcName}({paramStr})";
        return not ? $"{CoreStrings.Condition_Not}{condition}" : condition;
    }

    private static string ExtractFunctionName(string fullName)
    {
        // "Boolean IsGlobalValue(String, Operator, Int32)" → "IsGlobalValue"
        var parenIdx = fullName.IndexOf('(');
        var nameSection = parenIdx > 0 ? fullName[..parenIdx] : fullName;
        var lastSpace = nameSection.LastIndexOf(' ');
        return lastSpace >= 0 ? nameSection[(lastSpace + 1)..] : nameSection;
    }
}
