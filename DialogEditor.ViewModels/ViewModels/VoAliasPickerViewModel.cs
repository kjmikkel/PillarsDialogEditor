using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Audio;
using DialogEditor.Core.GameData;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// <summary>One selectable node in the VO alias picker.</summary>
public record VoAliasPickerRow(
    int NodeId, string SpeakerName, string TextPreview, string? DerivedAlias, bool WemExists)
{
    public bool   IsPickable => DerivedAlias is not null;
    public string WemGlyph   => WemExists ? "✓" : "✗";
}

/// <summary>
/// Backs VoAliasPickerWindow: choose a conversation, then a node whose recording
/// this node should reuse. The alias is DERIVED from the picked node's canonical
/// path (VoPathResolver.ExpectedRelativePath) — never typed — so it always
/// resolves under the game's Voices folder. Conversations are parsed one at a
/// time on selection; nothing is bulk-loaded, since PoE2 has ~2000 conversation
/// files and eagerly parsing all of them just to populate a picker list would be
/// a needless multi-second stall.
/// </summary>
public partial class VoAliasPickerViewModel : ObservableObject
{
    private readonly IGameDataProvider _provider;
    private readonly string _voicesRoot;
    private List<VoAliasPickerRow> _allRows = [];

    public IReadOnlyList<ConversationFile> AllConversations { get; }
    public ObservableCollection<ConversationFile> VisibleConversations { get; } = [];
    public ObservableCollection<VoAliasPickerRow> VisibleRows { get; } = [];

    [ObservableProperty] private string _conversationFilter = string.Empty;
    [ObservableProperty] private string _nodeFilter         = string.Empty;
    [ObservableProperty] private ConversationFile? _selectedConversation;
    [ObservableProperty] private VoAliasPickerRow? _selectedRow;

    /// The alias the dialog returns; null until a pickable row is selected.
    public string? ResultAlias => SelectedRow?.DerivedAlias;

    public VoAliasPickerViewModel(IGameDataProvider provider, string gameRoot, string? currentAlias)
    {
        _provider   = provider;
        _voicesRoot = VoPathResolver.VoicesRoot(gameRoot);
        AllConversations = provider.EnumerateConversations()
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        RefreshVisibleConversations();

        // "Change…" pre-selects the current target when the alias parses, so the
        // modder lands straight on the row they're re-pointing rather than an
        // empty picker.
        if (VoAliasParse.TryParse(currentAlias) is { } target)
        {
            SelectedConversation = AllConversations.FirstOrDefault(f =>
                string.Equals(f.Name, target.Conversation, StringComparison.OrdinalIgnoreCase));
            SelectedRow = _allRows.FirstOrDefault(r => r.NodeId == target.NodeId);
        }
    }

    partial void OnConversationFilterChanged(string value) => RefreshVisibleConversations();
    partial void OnNodeFilterChanged(string value)         => RefreshVisibleRows();
    partial void OnSelectedRowChanged(VoAliasPickerRow? value)
        => OnPropertyChanged(nameof(ResultAlias));

    partial void OnSelectedConversationChanged(ConversationFile? value)
    {
        SelectedRow = null;
        _allRows = [];
        if (value is not null)
        {
            try
            {
                var conv = _provider.LoadConversation(value);
                _allRows = conv.Nodes.Select(n =>
                {
                    var derived = VoPathResolver.ExpectedRelativePath(
                        n.SpeakerGuid, "", n.NodeId, value.Name);
                    var wem = derived is not null
                              && File.Exists(Path.Combine(_voicesRoot, derived + ".wem"));
                    var speaker = SpeakerNameService.FindByGuid(n.SpeakerGuid)?.Name
                                  ?? n.SpeakerCategory.ToString();
                    var text = conv.Strings.Get(n.NodeId)?.DefaultText ?? string.Empty;
                    // ExpectedRelativePath uses Path.Combine, which yields '\' on
                    // Windows. Shipped PoE2 ExternalVO values — and the alias-index /
                    // shared-count logic that compares alias strings literally — always
                    // use '/'. Normalize here so aliases picked on Windows match their
                    // forward-slash siblings once persisted into ExternalVO.
                    var alias = derived?.Replace('\\', '/');
                    return new VoAliasPickerRow(n.NodeId, speaker, text, alias, wem);
                }).ToList();
            }
            catch (Exception ex)
            {
                // Loading a conversation on selection can fail for a malformed
                // bundle on disk; leave the row list empty rather than crash the
                // picker window.
                AppLog.Warn($"Alias picker: could not load '{value.Name}': {ex.Message}");
            }
        }
        RefreshVisibleRows();
    }

    private void RefreshVisibleConversations()
    {
        VisibleConversations.Clear();
        foreach (var f in AllConversations)
            if (string.IsNullOrWhiteSpace(ConversationFilter)
                || f.Name.Contains(ConversationFilter, StringComparison.OrdinalIgnoreCase))
                VisibleConversations.Add(f);
    }

    private void RefreshVisibleRows()
    {
        VisibleRows.Clear();
        foreach (var r in _allRows)
            if (string.IsNullOrWhiteSpace(NodeFilter)
                || r.TextPreview.Contains(NodeFilter, StringComparison.OrdinalIgnoreCase)
                || r.NodeId.ToString().Contains(NodeFilter, StringComparison.Ordinal))
                VisibleRows.Add(r);
    }
}
