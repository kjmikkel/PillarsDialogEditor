using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DialogEditor.Tests.Helpers;

/// <summary>One literal the scanner considers user-visible and unlocalised.</summary>
public sealed record LiteralOffender(string File, int Line, string Text)
{
    public override string ToString() => $"{File}:{Line}: \"{Text}\"";
}

/// <summary>
/// Roslyn-based scanner behind NoHardcodedUiStringsInCodeTests. Regex cannot do this
/// job: the rule is "flag prose unless an enclosing declaration opts out via
/// [NotLocalised]", and associating a literal with its containing member needs a real
/// syntax tree. Roslyn also gives us comment/verbatim/raw-string handling for free.
/// </summary>
public static class HardcodedStringScanner
{
    public static IReadOnlyList<LiteralOffender> Scan(string source, string fileLabel = "test.cs")
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var offenders = new List<LiteralOffender>();

        foreach (var node in root.DescendantNodes())
        {
            var text = node switch
            {
                LiteralExpressionSyntax l when l.IsKind(SyntaxKind.StringLiteralExpression)
                    => l.Token.ValueText,
                InterpolatedStringExpressionSyntax i => Flatten(i),
                _ => null
            };
            if (text is null) continue;
            if (IsExempt(node)) continue;
            if (!LooksLikeUserVisibleText(text)) continue;

            var line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
            offenders.Add(new LiteralOffender(fileLabel, line, text));
        }
        return offenders;
    }

    // A translator is shown the whole sentence, so holes collapse to {0}, {1}, …
    // Judged fragment-by-fragment, "Node {Id} — {Snippet}" would look like the two
    // non-words "Node " and " — " and slip through — the very bug this guard exists for.
    private static string Flatten(InterpolatedStringExpressionSyntax node)
    {
        var sb = new StringBuilder();
        var hole = 0;
        foreach (var part in node.Contents)
        {
            if (part is InterpolatedStringTextSyntax t) sb.Append(t.TextToken.ValueText);
            else sb.Append('{').Append(hole++).Append('}');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Structural exemptions — things that are categorically not UI prose regardless of
    /// wording, so they never reach the predicate and never need a [NotLocalised] marker.
    /// </summary>
    private static bool IsExempt(SyntaxNode node)
    {
        // The marker's own reason string, [Obsolete("…")], etc.
        if (node.Ancestors().OfType<AttributeSyntax>().Any()) return true;

        // Under a declaration that opted out. BaseTypeDeclarationSyntax is itself a
        // MemberDeclarationSyntax, so one walk covers both members and their types.
        foreach (var decl in node.Ancestors().OfType<MemberDeclarationSyntax>())
            if (decl.AttributeLists.SelectMany(a => a.Attributes).Any(IsNotLocalised))
                return true;

        if (node.Parent is ArgumentSyntax arg && arg.Parent is ArgumentListSyntax args)
        {
            // Developer diagnostics: never shown to a player, never translated.
            if (args.Parent is InvocationExpressionSyntax inv
                && Receiver(inv) is "AppLog") return true;
            if (args.Parent is ObjectCreationExpressionSyntax oce
                && oce.Type.ToString().EndsWith("Exception", StringComparison.Ordinal)) return true;

            // Loc.Get("Key") / Loc.Format("Key", …) — the FIRST argument is a resource
            // key, i.e. localisation being done correctly. Later arguments are real
            // values and stay in scope.
            if (args.Parent is InvocationExpressionSyntax loc
                && Receiver(loc) is "Loc"
                && args.Arguments.IndexOf(arg) == 0) return true;
        }
        return false;
    }

    private static bool IsNotLocalised(AttributeSyntax a)
    {
        var name = a.Name.ToString();
        var leaf = name[(name.LastIndexOf('.') + 1)..];
        return leaf is "NotLocalised" or "NotLocalisedAttribute";
    }

    private static string? Receiver(InvocationExpressionSyntax inv) =>
        inv.Expression is MemberAccessExpressionSyntax m ? m.Expression.ToString() : null;

    // Format-specifier soup ("yyyy-MM-dd HH:mm:ss"), paths, masks, URLs, file extensions.
    private static readonly Regex NotProse = new(
        @"[\/]|://|^\*\.|^[\syMdHmsfFtzK:.\-/]+$|\.(cs|axaml|json|xml|csv|txt|bak|wem|ogg|ico|stringtable)",
        RegexOptions.Compiled);

    // "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats" — an assembly-qualified
    // type name is the only identifier shape that legitimately contains a space.
    private static readonly Regex AssemblyQualifiedName = new(
        @"^[A-Za-z_][\w.]*\s*,\s*[A-Za-z_][\w.]*$", RegexOptions.Compiled);

    private static readonly Regex Word = new(@"[A-Za-z]{3,}", RegexOptions.Compiled);

    /// <summary>
    /// THE PREDICATE — the judgement call about what counts as user-visible prose.
    /// Anything it returns true for must flow through Loc, or sit under a declaration
    /// marked [NotLocalised("reason")].
    ///
    /// The rule: a real word (3+ letters) separated by a space, minus four shapes that
    /// wear spaces without being sentences. The space requirement is what separates a
    /// sentence from an identifier — it keeps "Node {0} — {1}" and "Default Text" while
    /// dropping "node_{0}", "Speaker_Guid" and "utf-8".
    ///
    /// TUNING POINT: widen or narrow here. Every change is covered by the table of
    /// HardcodedStringScannerTests cases, including one that pins the FlowIssueViewModel
    /// bug shape so tightening can never silently re-open it.
    /// </summary>
    public static bool LooksLikeUserVisibleText(string literal)
    {
        if (literal.Length < 4) return false;
        if (!literal.Contains(' ')) return false;          // identifier, key, or glyph
        if (!Word.IsMatch(literal)) return false;          // no real word in it
        if (literal.Any(c => c is '\"' or '\n')) return false;   // data template
        if (AssemblyQualifiedName.IsMatch(literal)) return false;
        return !NotProse.IsMatch(literal);
    }
}
