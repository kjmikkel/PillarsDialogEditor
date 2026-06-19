using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class ParameterValueViewModel : ObservableObject
{
    public string Name        { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Type        { get; init; } = string.Empty;
    public IReadOnlyList<string>? Options { get; init; }
    public IReadOnlyList<string>? Values  { get; init; }
    public string LookupKind { get; init; } = string.Empty;

    [ObservableProperty] private string _value = string.Empty;

    // True when Options are display labels and Values are the actual stored strings.
    public bool IsLabeledEnum => Values is { Count: > 0 } && Options is { Count: > 0 }
                               && Values.Count == Options.Count;

    /// The value to serialise into the condition node. For labeled enums this is
    /// the entry from Values[] that corresponds to the currently selected label;
    /// for all other types it is Value itself.
    public string EffectiveValue
    {
        get
        {
            if (!IsLabeledEnum) return Value;
            var idx = Options!.ToList().IndexOf(Value);
            return idx >= 0 ? Values![idx] : Value;
        }
    }

    // For the condition editor window
    public bool IsEnum
        => (Options is { Count: > 0 }) || Type == "Operator" || Type == "Boolean";

    public bool HasLookup => LookupKind.Length > 0;

    public bool   IsText      => !IsEnum && !HasLookup;
    public bool   HasTypeHint => !string.IsNullOrEmpty(TypeHint);

    // Suggestions for lookup-typed parameters: DisplayName strings so the AutoCompleteBox
    // FilterMode=Contains matches on both the name and the raw stored value fragment.
    public IReadOnlyList<string> Suggestions =>
        HasLookup
            ? GameDataNameService.Get(LookupKind).Select(e => e.DisplayName).ToList()
            : [];

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
        "ObjectGuid"     => "GUID of an in-scene game object. Type a name or GUID prefix to search, or paste any GUID directly.",
        "Guid"           => "GUID value. Type a name or GUID prefix to search, or paste any GUID directly.",
        "Conversation"   => "Conversation filename without extension (e.g. edér)",
        "Quest"          => "Quest filename without extension",
        "GameData"       => "Asset GUID — check the game data files for the correct value",
        _ when Type.StartsWith("Enum:") =>
            $"Enum value — type: {Type["Enum:".Length..].Replace('+', '.')}",
        _ => string.IsNullOrEmpty(Type) ? string.Empty : $"Type: {Type}"
    };

    // When the user selects a suggestion from the AutoCompleteBox, Text is set to the
    // display string (DisplayName). Normalise it back to the StoredValue so the persisted
    // value is always the raw identifier, never the display label.
    partial void OnValueChanged(string value)
    {
        if (!HasLookup) return;
        var entry = GameDataNameService.Get(LookupKind)
            .FirstOrDefault(e => e.DisplayName == value);
        if (entry is not null)
            Value = entry.StoredValue;
    }
}

public partial class ConditionRowViewModel : ObservableObject
{
    public static IReadOnlyList<string> OperatorOptions
        => [Loc.Get("Option_ConditionAnd"), Loc.Get("Option_ConditionOr")];

    [ObservableProperty] private bool   _not;
    [ObservableProperty] private string _operator = "And";

    public string FullName { get; }

    private readonly string _fixedDisplayName = string.Empty;
    private ConditionBranch? _branch;

    /// Human-readable label. For branches, re-computed from the current branch
    /// components so it stays in sync after UpdateBranchComponents().
    public string DisplayName => IsBranch ? _branch!.Format() : _fixedDisplayName;

    /// True when this row wraps a leaf condition; false for a ConditionBranch.
    public bool IsLeaf   { get; }
    public bool IsBranch => !IsLeaf;

    /// The child conditions of this branch row. Empty list for leaf rows.
    public IReadOnlyList<ConditionNode> BranchComponents
        => _branch?.Components ?? [];

    /// Replaces the branch's components and notifies the UI to refresh DisplayName.
    public void UpdateBranchComponents(IReadOnlyList<ConditionNode> components)
    {
        if (!IsBranch) return;
        _branch = _branch! with { Components = components };
        OnPropertyChanged(nameof(DisplayName));
    }

    public ObservableCollection<ParameterValueViewModel> Parameters { get; }

    /// Constructor for ConditionBranch pass-through rows.
    public ConditionRowViewModel(ConditionBranch branch)
    {
        IsLeaf    = false;
        _branch   = branch;
        _not      = branch.Not;
        _operator = branch.Operator;
        FullName  = "(grouped)";
        Parameters = [];
    }

    /// Returns the condition node this row represents (leaf or original branch).
    public ConditionNode ToNode() =>
        IsLeaf ? ToLeaf() : (ConditionNode)_branch! with { Not = Not, Operator = Operator };

    public ConditionRowViewModel(ConditionLeaf leaf, ConditionEntry? catalogueEntry)
    {
        IsLeaf             = true;
        FullName           = leaf.FullName;
        _fixedDisplayName  = catalogueEntry?.DisplayName ?? StripReturnType(leaf.FullName);
        _not        = leaf.Not;
        _operator   = leaf.Operator;

        if (catalogueEntry is not null)
        {
            // Align values from the leaf with catalogue parameter definitions
            Parameters = new(catalogueEntry.Parameters
                .Select((p, i) =>
                {
                    var stored = i < leaf.Parameters.Count ? leaf.Parameters[i] : p.Default;
                    // For labeled enums, display the label rather than the stored value
                    var display = (p.Values is { Count: > 0 } && p.Options is { Count: > 0 })
                        ? LookupLabel(stored, p.Options, p.Values) ?? stored
                        : stored;
                    return new ParameterValueViewModel
                    {
                        Name        = p.Name,
                        Description = p.Description,
                        Type        = p.Type,
                        Options     = p.Options,
                        Values      = p.Values,
                        LookupKind  = p.LookupKind ?? string.Empty,
                        Value       = display,
                    };
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
        new(FullName, Parameters.Select(p => p.EffectiveValue).ToList(), Not, Operator);

    private static string? LookupLabel(
        string stored, IReadOnlyList<string> options, IReadOnlyList<string> values)
    {
        for (var i = 0; i < values.Count; i++)
            if (string.Equals(values[i], stored, StringComparison.OrdinalIgnoreCase) && i < options.Count)
                return options[i];
        return null;
    }

    private static string StripReturnType(string fullName)
    {
        var afterSpace = fullName.Contains(' ') ? fullName[(fullName.IndexOf(' ') + 1)..] : fullName;
        return afterSpace.Contains('(') ? afterSpace[..afterSpace.IndexOf('(')] : afterSpace;
    }
}
