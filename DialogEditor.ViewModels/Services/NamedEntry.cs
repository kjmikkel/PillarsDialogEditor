namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Display string + stored value pair for AutoCompleteBox suggestions.
/// For GUID kinds: DisplayName = "Edér — guid", StoredValue = "guid".
/// For string kinds: DisplayName = StoredValue = variable/item name.
/// </summary>
public record NamedEntry(string DisplayName, string StoredValue);
