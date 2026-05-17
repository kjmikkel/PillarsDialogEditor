using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class ParameterValueViewModel : ObservableObject
{
    public string Name        { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Type        { get; init; } = string.Empty;
    public IReadOnlyList<string>? Options { get; init; }

    [ObservableProperty] private string _value = string.Empty;

    // For the condition editor window
    public bool IsEnum
        => (Options is { Count: > 0 }) || Type == "Operator" || Type == "Boolean";
    public bool   IsText      => !IsEnum;
    public bool   HasTypeHint => !string.IsNullOrEmpty(TypeHint);

    public IReadOnlyList<string> EnumOptions
    {
        get
        {
            if (Options is { Count: > 0 }) return Options;
            return Type switch
            {
                "Operator" => ["EqualTo", "NotEqualTo", "GreaterThan", "LessThan",
                               "GreaterThanOrEqualTo", "LessThanOrEqualTo"],
                "Boolean"  => ["true", "false"],
                _ => []
            };
        }
    }

    public string TypeHint => Type switch
    {
        "String"         => "Text string (e.g. a flag name, conversation name, or item name)",
        "Int32"          => "Integer number (whole number, no decimals)",
        "Single"         => "Decimal number (e.g. 1.5)",
        "Boolean"        => "true or false",
        "Operator"       => "Comparison operator: EqualTo, NotEqualTo, GreaterThan, LessThan, "
                          + "GreaterThanOrEqualTo, LessThanOrEqualTo",
        "GlobalVariable" => "Name of a global integer flag (e.g. npc_met_edér). "
                          + "Check GlobalVariables.csv for valid names.",
        "ObjectGuid"     => "GUID of an in-scene game object "
                          + "(e.g. 7d150000-0000-0000-0000-000000000000). "
                          + "Use the default companion GUIDs or an in-scene object GUID.",
        "Conversation"   => "Conversation filename without extension (e.g. edér)",
        "Quest"          => "Quest filename without extension",
        "GameData"       => "Asset GUID — check the game data files for the correct value",
        _ when Type.StartsWith("Enum:") =>
            $"Enum value — type: {Type["Enum:".Length..].Replace('+', '.')}",
        _ => string.IsNullOrEmpty(Type) ? string.Empty : $"Type: {Type}"
    };
}

public partial class ConditionRowViewModel : ObservableObject
{
    [ObservableProperty] private bool   _not;
    [ObservableProperty] private string _operator = "And";

    public string FullName    { get; }
    public string DisplayName { get; }

    /// True when this row wraps a ConditionBranch — editing is disabled,
    /// but the row can be moved or deleted and its node is committed unchanged.
    public bool IsLeaf   { get; }
    public bool IsBranch => !IsLeaf;

    private readonly ConditionBranch? _branch;

    public ObservableCollection<ParameterValueViewModel> Parameters { get; }

    /// Constructor for ConditionBranch pass-through rows.
    public ConditionRowViewModel(ConditionBranch branch)
    {
        IsLeaf      = false;
        _branch     = branch;
        _not        = branch.Not;
        _operator   = branch.Operator;
        FullName    = "(grouped)";
        DisplayName = branch.Format();
        Parameters  = [];
    }

    /// Returns the condition node this row represents (leaf or original branch).
    public ConditionNode ToNode() =>
        IsLeaf ? ToLeaf() : (ConditionNode)_branch! with { Not = Not, Operator = Operator };

    public ConditionRowViewModel(ConditionLeaf leaf, ConditionEntry? catalogueEntry)
    {
        IsLeaf      = true;
        FullName    = leaf.FullName;
        DisplayName = catalogueEntry?.DisplayName ?? StripReturnType(leaf.FullName);
        _not        = leaf.Not;
        _operator   = leaf.Operator;

        if (catalogueEntry is not null)
        {
            // Align values from the leaf with catalogue parameter definitions
            Parameters = new(catalogueEntry.Parameters
                .Select((p, i) => new ParameterValueViewModel
                {
                    Name        = p.Name,
                    Description = p.Description,
                    Type        = p.Type,
                    Options     = p.Options,
                    Value       = i < leaf.Parameters.Count ? leaf.Parameters[i] : p.Default,
                }));
        }
        else
        {
            // Unknown condition — one value row per existing parameter
            Parameters = new(leaf.Parameters
                .Select(v => new ParameterValueViewModel { Name = "Parameter", Value = v }));
        }
    }

    public ConditionLeaf ToLeaf() =>
        new(FullName, Parameters.Select(p => p.Value).ToList(), Not, Operator);

    private static string StripReturnType(string fullName)
    {
        var afterSpace = fullName.Contains(' ') ? fullName[(fullName.IndexOf(' ') + 1)..] : fullName;
        return afterSpace.Contains('(') ? afterSpace[..afterSpace.IndexOf('(')] : afterSpace;
    }
}
