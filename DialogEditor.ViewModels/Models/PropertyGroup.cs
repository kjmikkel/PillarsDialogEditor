namespace DialogEditor.ViewModels.Models;

public record PropertyGroup(string Name, IReadOnlyList<PropertyRow> Rows);
