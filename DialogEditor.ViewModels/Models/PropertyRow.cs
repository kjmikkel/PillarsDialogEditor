namespace DialogEditor.ViewModels.Models;

public record PropertyRow(
    string Label,
    string Value,
    PropertyValueStyle Style = PropertyValueStyle.Default);
