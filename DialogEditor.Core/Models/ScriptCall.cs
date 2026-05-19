namespace DialogEditor.Core.Models;

public enum ScriptCategory { Enter, Exit, Update }

public record ScriptCall(
    string                  FullName,
    IReadOnlyList<string>   Parameters,
    ScriptCategory          Category)
{
    /// Method name stripped of return type and parameter types for display.
    public string DisplayName
    {
        get
        {
            // "Void SetGlobalValue(String, Int32)" → "SetGlobalValue"
            var afterSpace = FullName.Contains(' ')
                ? FullName[(FullName.IndexOf(' ') + 1)..]
                : FullName;
            return afterSpace.Contains('(')
                ? afterSpace[..afterSpace.IndexOf('(')]
                : afterSpace;
        }
    }

    public string Format()
    {
        var paramStr = Parameters.Count == 0
            ? string.Empty
            : string.Join(", ", Parameters);
        return $"{DisplayName}({paramStr})";
    }
}
