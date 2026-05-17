using System.Text.Json.Serialization;

namespace DialogEditor.Core.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(ConditionLeaf),   typeDiscriminator: "leaf")]
[JsonDerivedType(typeof(ConditionBranch), typeDiscriminator: "branch")]

// Operator string for both PoE1 ("And"/"Or") and PoE2 (int mapped to "And"/"Or").
// Stored as a string so the model is format-agnostic.

/// Abstract base for a node in a condition tree.
public abstract record ConditionNode(bool Not, string Operator)
{
    /// Human-readable flat display string (used in the detail panel).
    public abstract string Format();

    /// Produce a flat list of every leaf condition (ignores grouping).
    public abstract IEnumerable<ConditionNode> Leaves();
}

/// A leaf condition — a single function call with parameters.
public sealed record ConditionLeaf(
    string FullName,
    IReadOnlyList<string> Parameters,
    bool Not,
    string Operator) : ConditionNode(Not, Operator)
{
    public override string Format()
    {
        var paramStr = Parameters.Count == 0
            ? string.Empty
            : string.Join(", ", Parameters);
        // Extract the method name: between the first space and the first '('
        // FullName example: "Boolean IsGlobalValue(String, Operator, Int32)"
        var afterSpace = FullName.Contains(' ')
            ? FullName[(FullName.IndexOf(' ') + 1)..]
            : FullName;
        var name = afterSpace.Contains('(')
            ? afterSpace[..afterSpace.IndexOf('(')]
            : afterSpace;
        var body = $"{name}({paramStr})";
        return Not ? $"NOT {body}" : body;
    }

    public override IEnumerable<ConditionNode> Leaves() { yield return this; }
}

/// A grouped / nested condition expression.
public sealed record ConditionBranch(
    IReadOnlyList<ConditionNode> Components,
    bool Not,
    string Operator) : ConditionNode(Not, Operator)
{
    public override string Format()
    {
        var inner = string.Join($" {Operator.ToUpperInvariant()} ",
            Components.Select(c => c.Format()));
        var wrapped = $"({inner})";
        return Not ? $"NOT {wrapped}" : wrapped;
    }

    public override IEnumerable<ConditionNode> Leaves()
        => Components.SelectMany(c => c.Leaves());
}

public static class ConditionNodeExtensions
{
    /// Produce the indented multi-line tree string stored in ConditionExpression.
    public static string FormatTree(
        this IReadOnlyList<ConditionNode> nodes,
        int depth = 0)
    {
        if (nodes.Count == 0) return string.Empty;
        var indent = new string(' ', depth * 2);
        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < nodes.Count; i++)
        {
            if (i > 0)
            {
                var op = nodes[i - 1].Operator.ToUpperInvariant();
                sb.Append($"{Environment.NewLine}{indent}{op} ");
            }

            var node = nodes[i];
            if (node is ConditionBranch branch)
            {
                var inner = branch.Components.FormatTree(depth + 1);
                var notPrefix = branch.Not ? "NOT " : "";
                sb.Append($"{notPrefix}({Environment.NewLine}{indent}  {inner}{Environment.NewLine}{indent})");
            }
            else
            {
                sb.Append(node.Format());
            }
        }
        return sb.ToString();
    }
}
