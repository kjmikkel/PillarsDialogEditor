namespace DialogEditor.ViewModels;

/// A node's canvas emphasis under the active search. None = normal (no search, or a
/// text-search match). Match = a condition/script-search hit (emphasis border). Dimmed =
/// faded because a search is active and this node is not a match. Both the canvas text
/// search and the condition/script search flow through this one state (last search wins).
public enum SearchMatchState { None, Match, Dimmed }
