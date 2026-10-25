using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Localisation;
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

    /// The value to serialise into the condition node.
    /// - Labeled enums: the Values[] entry matching the currently displayed label.
    /// - Lookup kinds:  the StoredValue for the displayed DisplayName (e.g. the raw
    ///   GUID when Value contains "Mercenary — guid"). Falls back to Value if not found.
    ///   This translation happens here rather than in OnValueChanged so that the
    ///   AutoCompleteBox text is never changed mid-navigation (which would shrink the
    ///   filtered list and crash the selection model's SelectedIndexIncrement).
    /// - All other types: Value itself.
    public string EffectiveValue
    {
        get
        {
            if (IsLabeledEnum)
            {
                var idx = Options!.ToList().IndexOf(Value);
                return idx >= 0 ? Values![idx] : Value;
            }
            if (HasLookup)
            {
                var entry = GameDataNameService.Get(LookupKind)
                    .FirstOrDefault(e => e.DisplayName == Value);
                return entry?.StoredValue ?? Value;
            }
            return Value;
        }
    }

    // For the condition editor window
    public bool IsEnum
        => (Options is { Count: > 0 }) || Type == "Operator" || Type == "Boolean";

    /// A comparison operator: a fixed list of plain labels, never free text (GitHub issue 266).
    /// A typo there would save an operator the game can't read.
    public bool IsFixedChoice => IsOperatorType(Type);

    /// Other enums keep the filterable free-text box, as their option lists can be long or
    /// incomplete.
    public bool IsFreeEnum => IsEnum && !IsFixedChoice;

    /// The bundled catalogues type operators "Enum:Operator"; "Operator" is the older form.
    public static bool IsOperatorType(string type) => type is "Operator" or "Enum:Operator";

    /// The operator names the game stores, in the order of its own enum
    /// (Game.GameData.Operator / Conditionals.Operator).
    public static IReadOnlyList<string> OperatorNames { get; } =
        ["EqualTo", "GreaterThan", "LessThan", "NotEqualTo", "GreaterThanOrEqualTo", "LessThanOrEqualTo"];

    /// The plain label for each of <see cref="OperatorNames"/> ("equals", "is at least").
    public static IReadOnlyList<string> OperatorLabels() =>
        OperatorNames.Select(n => Loc.Get("ConditionOperator_" + n)).ToList();

    /// The choices to offer and the values to save for a catalogue parameter. An operator is a
    /// labelled enum built from the game's names, whatever the catalogue lists: the bundled
    /// catalogues offer "Equals", which isn't one (GitHub issue 266).
    public static (IReadOnlyList<string>? Options, IReadOnlyList<string>? Values) Choices(ConditionParameter p) =>
        IsOperatorType(p.Type) ? (OperatorLabels(), OperatorNames) : (p.Options, p.Values);

    /// What to show for a stored value: the label of a labelled enum, else the value itself.
    /// A stored "Equals" (saved from the old catalogue option) reads as EqualTo, so saving the
    /// condition again repairs it.
    public static string Display(string type, string stored,
        IReadOnlyList<string>? options, IReadOnlyList<string>? values)
    {
        if (IsOperatorType(type) && string.Equals(stored, "Equals", StringComparison.OrdinalIgnoreCase))
            stored = "EqualTo";
        if (options is not { Count: > 0 } || values is not { Count: > 0 }) return stored;
        for (var i = 0; i < values.Count && i < options.Count; i++)
            if (string.Equals(values[i], stored, StringComparison.OrdinalIgnoreCase)) return options[i];
        return stored;
    }

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
                "Operator" => OperatorLabels(),
                "Boolean"  => ["true", "false"],
                _ => []
            };
        }
    }

    /// What this parameter expects, shown as the field's tooltip and exposed as its
    /// AutomationProperties.HelpText — so a screen reader reads it too. An unrecognised
    /// type falls back to naming the type; an EMPTY type yields no hint at all, because
    /// HasTypeHint drives the hint row's visibility and a resolved-but-blank string
    /// would show an empty tooltip.
    public string TypeHint => Type switch
    {
        "String"         => Loc.Get("ConditionHint_String"),
        "Int32"          => Loc.Get("ConditionHint_Int32"),
        "Single"         => Loc.Get("ConditionHint_Single"),
        "Boolean"        => Loc.Get("ConditionHint_Boolean"),
        "Operator"       => Loc.Get("ConditionHint_Operator"),
        "GlobalVariable" => Loc.Get("ConditionHint_GlobalVariable"),
        "ObjectGuid"     => Loc.Get("ConditionHint_ObjectGuid"),
        "Guid"           => Loc.Get("ConditionHint_Guid"),
        "Conversation"   => Loc.Get("ConditionHint_Conversation"),
        "Quest"          => Loc.Get("ConditionHint_Quest"),
        "GameData"       => Loc.Get("ConditionHint_GameData"),
        _ when Type.StartsWith("Enum:") =>
            Loc.Format("ConditionHint_Enum", Type["Enum:".Length..].Replace('+', '.')),
        _ => string.IsNullOrEmpty(Type) ? string.Empty : Loc.Format("ConditionHint_Fallback", Type)
    };

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
    [NotLocalised("FullName is a C# reflection signature, not a label; a branch row has "
                + "none, and the UI shows DisplayName (the formatted branch) instead")]
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
                    var stored     = i < leaf.Parameters.Count ? leaf.Parameters[i] : p.Default;
                    var lookupKind = p.LookupKind ?? string.Empty;
                    var (options, values) = ParameterValueViewModel.Choices(p);
                    string display;
                    if (lookupKind.Length > 0)
                    {
                        // Initialise the text box with the human-readable DisplayName so the
                        // user immediately sees "Mercenary — guid" rather than a raw GUID.
                        // EffectiveValue translates back to StoredValue on save.
                        display = GameDataNameService.Get(lookupKind)
                            .FirstOrDefault(e => e.StoredValue == stored)?.DisplayName ?? stored;
                    }
                    else
                    {
                        // For labeled enums, display the label rather than the stored value
                        display = ParameterValueViewModel.Display(p.Type, stored, options, values);
                    }
                    return new ParameterValueViewModel
                    {
                        Name        = p.Name,
                        Description = p.Description,
                        Type        = p.Type,
                        Options     = options,
                        Values      = values,
                        LookupKind  = lookupKind,
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

    private static string StripReturnType(string fullName)
    {
        var afterSpace = fullName.Contains(' ') ? fullName[(fullName.IndexOf(' ') + 1)..] : fullName;
        return afterSpace.Contains('(') ? afterSpace[..afterSpace.IndexOf('(')] : afterSpace;
    }
}
