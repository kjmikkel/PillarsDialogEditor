using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Avalonia.Controls;
using DialogEditor.Core.Logging;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Behaviors;

/// Right-click menu on a misspelt word in a TextBox (GitHub issue 313): up to
/// <see cref="SpellingMenu.MaxSuggestions"/> suggestions (choosing one replaces the word),
/// Add to dictionary / Add to project dictionary, then Cut / Copy / Paste.
///
/// The <see cref="FinderProperty"/> delegate says what to offer at a text index; this behaviour
/// only builds and opens the menu. Anywhere that is not a misspelt word the request is left
/// unhandled, so the TextBox's normal context menu opens as before. Suggestions edit
/// <c>TextBox.Text</c> directly, which keeps the box's own undo stack; the LostFocus binding
/// commits the change to the view model as for any typing. The caret is never moved by the
/// right-click itself.
public static class SpellingContextMenu
{
    public static readonly AttachedProperty<Func<string, int, SpellingMenu?>?> FinderProperty =
        AvaloniaProperty.RegisterAttached<TextBox, Func<string, int, SpellingMenu?>?>(
            "Finder", typeof(SpellingContextMenu));

    public static void SetFinder(TextBox t, Func<string, int, SpellingMenu?>? v) => t.SetValue(FinderProperty, v);
    public static Func<string, int, SpellingMenu?>? GetFinder(TextBox t) => t.GetValue(FinderProperty);

    static SpellingContextMenu()
    {
        // Tunnel so we run before the TextBox's own handler that opens its ContextFlyout;
        // marking the event handled then stops that flyout.
        Control.ContextRequestedEvent.AddClassHandler<TextBox>(OnContextRequested, RoutingStrategies.Tunnel);
    }

    private static void OnContextRequested(TextBox box, ContextRequestedEventArgs e)
    {
        if (e.Handled || GetFinder(box) is null) return;
        try
        {
            var presenter = box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
            int index;
            if (presenter is not null && e.TryGetPosition(presenter, out var point))
                index = presenter.TextLayout.HitTestPoint(point).TextPosition;
            else
                index = box.CaretIndex;

            var menu = CreateMenu(box, index);
            if (menu is null) return;      // not a misspelt word: the normal menu opens
            e.Handled = true;
            menu.Open(box);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Warn($"Spelling context menu failed: {ex.Message}");
        }
    }

    /// The spelling menu for the word at <paramref name="index"/>, or null when there is no
    /// finder or no misspelt word there.
    public static ContextMenu? CreateMenu(TextBox box, int index)
    {
        var finder = GetFinder(box);
        if (finder is null) return null;
        var text = box.Text ?? "";
        var info = finder(text, index);
        if (info is null) return null;

        var menu = new ContextMenu();
        var items = new List<Control>();

        if (info.Suggestions.Count == 0)
        {
            items.Add(Item("SpellMenu_NoSuggestions", Loc.Get("SpellMenu_NoSuggestions"),
                Loc.Get("ToolTip_SpellMenu_NoSuggestions"), null, enabled: false));
        }
        for (var i = 0; i < info.Suggestions.Count; i++)
        {
            var suggestion = info.Suggestions[i];
            items.Add(Item($"SpellMenu_Suggestion_{i}", suggestion,
                Loc.Format("ToolTip_SpellMenu_Suggestion", info.Word, suggestion),
                new RelayCommand(() => Replace(box, info, suggestion)),
                enabled: !box.IsReadOnly));
        }

        items.Add(new Separator());
        items.Add(Item("SpellMenu_AddToDictionary", Loc.Get("Button_AddToDictionary"),
            Loc.Get("ToolTip_AddToDictionary"),
            info.AddToDictionary is { } add ? new RelayCommand(add) : null,
            enabled: info.AddToDictionary is not null));
        if (info.AddToProjectDictionary is { } addProject)
            items.Add(Item("SpellMenu_AddToProjectDictionary", Loc.Get("Button_AddToProjectDictionary"),
                Loc.Get("ToolTip_AddToProjectDictionary"), new RelayCommand(addProject), enabled: true));

        items.Add(new Separator());
        items.Add(Item("SpellMenu_Cut", Loc.Get("SpellMenu_Cut"), Loc.Get("ToolTip_SpellMenu_Cut"),
            new RelayCommand(box.Cut), enabled: box.CanCut));
        items.Add(Item("SpellMenu_Copy", Loc.Get("SpellMenu_Copy"), Loc.Get("ToolTip_SpellMenu_Copy"),
            new RelayCommand(box.Copy), enabled: box.CanCopy));
        items.Add(Item("SpellMenu_Paste", Loc.Get("SpellMenu_Paste"), Loc.Get("ToolTip_SpellMenu_Paste"),
            new RelayCommand(box.Paste), enabled: box.CanPaste));

        menu.ItemsSource = items;
        return menu;
    }

    private static void Replace(TextBox box, SpellingMenu info, string suggestion)
    {
        var text = box.Text ?? "";
        if (info.Start < 0 || info.Length < 0 || info.Start + info.Length > text.Length) return;
        box.Text = string.Concat(text.AsSpan(0, info.Start), suggestion, text.AsSpan(info.Start + info.Length));
        box.CaretIndex = info.Start + suggestion.Length;
    }

    private static AccessibleMenuItem Item(string id, string header, string tip,
        System.Windows.Input.ICommand? command, bool enabled)
    {
        var item = new AccessibleMenuItem { Header = header, Command = command, IsEnabled = enabled };
        AutomationProperties.SetAutomationId(item, id);
        ToolTip.SetTip(item, tip);
        return item;
    }
}
