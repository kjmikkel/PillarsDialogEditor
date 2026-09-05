using System.Text;

namespace DialogEditor.UiaMcp.Core;

/// <summary>
/// Escapes literal text for System.Windows.Forms.SendKeys, whose syntax claims
/// + ^ % ~ ( ) { } [ ]. An unescaped metacharacter does not raise an error — it silently
/// sends different keys than intended, so this is worth doing exactly once and testing.
///
/// The square brackets matter most here: dialogue text carries substitution tokens like
/// [playername], so unescaped text typed into a node would be mangled rather than rejected.
/// </summary>
public static class SendKeysEscaper
{
    private const string Metacharacters = "+^%~(){}[]";

    public static string EscapeLiteral(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (Metacharacters.Contains(c)) sb.Append('{').Append(c).Append('}');
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
