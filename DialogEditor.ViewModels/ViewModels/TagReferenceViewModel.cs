using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public enum TagGameFilter { PoE1, PoE2, Both }

/// A game-filter choice with its localised label (ComboBox item).
public sealed record TagGameOption(TagGameFilter Value, string Label)
{
    public override string ToString() => Label;
}

/// One tag entry prepared for display.
public sealed class TagRowViewModel(TagEntry entry, bool showBadge)
{
    public string  Name        => entry.Name;
    public string  Description => entry.Description;
    public string? Example     => entry.Example;
    public bool    HasExample  => !string.IsNullOrEmpty(entry.Example);
    public string? Notes       => entry.Notes;
    public bool    HasNotes    => !string.IsNullOrEmpty(entry.Notes);

    /// Engine-supported token that never occurs in shipped dialog (count 0).
    public bool   IsEngineOnly    => entry.Kind == "Token" && entry.Count == 0;
    public string EngineOnlyLabel => Loc.Get("TagRef_EngineOnly");

    /// Game badges are shown only in the "Both" filter, where entries mix.
    public bool   ShowBadge  { get; } = showBadge;
    public string GamesBadge => string.Join(" · ", entry.Games.Select(g =>
        Loc.Get(string.Equals(g, "poe1", StringComparison.OrdinalIgnoreCase)
            ? "TagRef_BadgePoE1" : "TagRef_BadgePoE2")));
}

/// A category of token rows with its localised header.
public sealed class TagGroupViewModel(string header, IReadOnlyList<TagRowViewModel> rows)
{
    public string Header { get; } = header;
    public IReadOnlyList<TagRowViewModel> Rows { get; } = rows;
}

/// Game-aware, searchable view over the tag vocabulary (TagCatalogue).
/// Pure logic — the window binds to the computed collections.
/// Spec: docs/superpowers/specs/2026-07-05-tag-reference-window-design.md
public sealed partial class TagReferenceViewModel : ObservableObject
{
    // Fixed display order for token categories.
    private static readonly string[] CategoryOrder =
        ["Player", "CharacterReference", "ShipDuel", "Other"];

    private readonly TagCatalogue _catalogue;

    public IReadOnlyList<TagGameOption> GameOptions { get; }

    [ObservableProperty] private TagGameOption _selectedGame;
    [ObservableProperty] private string        _searchText = string.Empty;

    public TagReferenceViewModel(string activeGameId, TagCatalogue? catalogue = null)
    {
        _catalogue = catalogue ?? TagCatalogue.Instance;
        GameOptions =
        [
            new(TagGameFilter.PoE1, Loc.Get("TagRef_GamePoE1")),
            new(TagGameFilter.PoE2, Loc.Get("TagRef_GamePoE2")),
            new(TagGameFilter.Both, Loc.Get("TagRef_GameBoth")),
        ];
        // Follow the open game folder; PoE2 is the default vocabulary otherwise.
        var initial = string.Equals(activeGameId, "poe1", StringComparison.OrdinalIgnoreCase)
            ? TagGameFilter.PoE1 : TagGameFilter.PoE2;
        _selectedGame = GameOptions.First(o => o.Value == initial);
    }

    partial void OnSelectedGameChanged(TagGameOption value) => Refresh();
    partial void OnSearchTextChanged(string value)          => Refresh();

    private void Refresh()
    {
        OnPropertyChanged(nameof(TokenGroups));
        OnPropertyChanged(nameof(MarkupRows));
        OnPropertyChanged(nameof(ConventionRows));
        OnPropertyChanged(nameof(HasTokens));
        OnPropertyChanged(nameof(HasMarkup));
        OnPropertyChanged(nameof(HasConventions));
        OnPropertyChanged(nameof(HasNoResults));
    }

    private bool ShowBadges => SelectedGame.Value == TagGameFilter.Both;

    private bool MatchesGame(TagEntry e) => SelectedGame.Value switch
    {
        TagGameFilter.PoE1 => e.Games.Any(g => string.Equals(g, "poe1", StringComparison.OrdinalIgnoreCase)),
        TagGameFilter.PoE2 => e.Games.Any(g => string.Equals(g, "poe2", StringComparison.OrdinalIgnoreCase)),
        _                  => true,
    };

    private bool MatchesSearch(TagEntry e)
        => string.IsNullOrWhiteSpace(SearchText)
           || e.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
           || e.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

    private IEnumerable<TagEntry> Visible(string kind)
        => _catalogue.All.Where(e => e.Kind == kind && MatchesGame(e) && MatchesSearch(e));

    public IReadOnlyList<TagGroupViewModel> TokenGroups =>
        CategoryOrder
            .Select(c => new TagGroupViewModel(
                Loc.Get($"TagCategory_{c}"),
                Visible("Token").Where(e => e.Category == c)
                    .Select(e => new TagRowViewModel(e, ShowBadges)).ToList()))
            .Where(g => g.Rows.Count > 0)
            .ToList();

    public IReadOnlyList<TagRowViewModel> MarkupRows =>
        Visible("Markup").Select(e => new TagRowViewModel(e, ShowBadges)).ToList();

    public IReadOnlyList<TagRowViewModel> ConventionRows =>
        Visible("Convention").Select(e => new TagRowViewModel(e, ShowBadges)).ToList();

    public bool HasTokens      => TokenGroups.Count > 0;
    public bool HasMarkup      => MarkupRows.Count > 0;
    public bool HasConventions => ConventionRows.Count > 0;
    public bool HasNoResults   => !HasTokens && !HasMarkup && !HasConventions;
}
