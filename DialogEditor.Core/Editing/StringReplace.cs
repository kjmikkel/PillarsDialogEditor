using System.Text;

namespace DialogEditor.Core.Editing;

public static class StringReplace
{
    public static string ReplaceAll(
        string source, string search, string replacement, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(search)) return source;
        var result = new StringBuilder();
        var pos    = 0;
        while (true)
        {
            var idx = source.IndexOf(search, pos, comparison);
            if (idx < 0) { result.Append(source, pos, source.Length - pos); break; }
            result.Append(source, pos, idx - pos);
            result.Append(replacement);
            pos = idx + search.Length;
        }
        return result.ToString();
    }
}
