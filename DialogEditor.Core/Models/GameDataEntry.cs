namespace DialogEditor.Core.Models;

/// <summary>
/// Raw name + identifier pair returned by game-data parsers.
/// Id is empty for string-keyed kinds (GlobalVariable, PoE1 item names).
/// </summary>
public record GameDataEntry(string Id, string Name);
