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

    [ObservableProperty] private string _value = string.Empty;
}

public partial class ConditionRowViewModel : ObservableObject
{
    [ObservableProperty] private bool   _not;
    [ObservableProperty] private string _operator = "And";

    public string FullName    { get; }
    public string DisplayName { get; }

    public ObservableCollection<ParameterValueViewModel> Parameters { get; }

    public ConditionRowViewModel(ConditionLeaf leaf, ConditionEntry? catalogueEntry)
    {
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
