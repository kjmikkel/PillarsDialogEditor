using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DialogEditor.Avalonia.Behaviors;
using DialogEditor.Avalonia.Controls;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Views;

/// GitHub issue 313: the right-click menu on a misspelt word in Node Details.
public class SpellingContextMenuTests
{
    private static readonly List<string> Added = [];
    private static readonly List<string> AddedToProject = [];

    // A stand-in: "captian" is misspelt everywhere, with two suggestions.
    private static SpellingMenu? Finder(string text, int index, bool project)
    {
        for (var i = text.IndexOf("captian", StringComparison.Ordinal); i >= 0;
             i = text.IndexOf("captian", i + 1, StringComparison.Ordinal))
            if (index >= i && index <= i + 7)
                return new SpellingMenu("captian", i, 7, ["captain", "caption"],
                    () => Added.Add("captian"),
                    project ? () => AddedToProject.Add("captian") : null);
        return null;
    }

    private static TextBox Setup(string text, bool project = true)
    {
        Loc.Configure(new StubStringProvider());
        Added.Clear(); AddedToProject.Clear();
        var tb = new TextBox { Text = text };
        SpellingContextMenu.SetFinder(tb, (t, i) => Finder(t, i, project));
        var window = new Window { Width = 600, Height = 200, Content = tb };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return tb;
    }

    private static List<AccessibleMenuItem> Items(ContextMenu menu)
        => menu.Items.OfType<AccessibleMenuItem>().ToList();

    [AvaloniaFact]
    public void NotOnAMisspeltWord_NoSpellingMenu()
        // The TextBox's normal menu is left alone.
        => Assert.Null(SpellingContextMenu.CreateMenu(Setup("The captian glows"), 14));

    [AvaloniaFact]
    public void OnAMisspeltWord_SuggestionsThenDictionaryThenClipboard()
    {
        var menu = SpellingContextMenu.CreateMenu(Setup("The captian glows"), 6)!;
        var ids = Items(menu).Select(AutomationProperties.GetAutomationId).ToList();
        Assert.Equal(
            ["SpellMenu_Suggestion_0", "SpellMenu_Suggestion_1",
             "SpellMenu_AddToDictionary", "SpellMenu_AddToProjectDictionary",
             "SpellMenu_Cut", "SpellMenu_Copy", "SpellMenu_Paste"], ids);
        Assert.Equal("captain", Items(menu)[0].Header);
        Assert.Equal(2, menu.Items.OfType<Separator>().Count());
    }

    [AvaloniaFact]
    public void EveryItem_HasATooltip()
    {
        var menu = SpellingContextMenu.CreateMenu(Setup("The captian glows"), 6)!;
        Assert.All(Items(menu), i => Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(i) as string)));
    }

    [AvaloniaFact]
    public void NoProject_NoProjectDictionaryItem()
    {
        var menu = SpellingContextMenu.CreateMenu(Setup("The captian glows", project: false), 6)!;
        Assert.DoesNotContain(Items(menu), i => AutomationProperties.GetAutomationId(i) == "SpellMenu_AddToProjectDictionary");
    }

    [AvaloniaFact]
    public void ChoosingASuggestion_ReplacesTheWord_AndPutsTheCaretAfterIt()
    {
        var tb = Setup("The captian glows");
        var menu = SpellingContextMenu.CreateMenu(tb, 6)!;
        Items(menu)[0].Command!.Execute(null);
        Assert.Equal("The captain glows", tb.Text);
        Assert.Equal(11, tb.CaretIndex);
    }

    [AvaloniaFact]
    public void AddToDictionary_CallsTheViewModel()
    {
        var tb = Setup("The captian glows");
        var menu = SpellingContextMenu.CreateMenu(tb, 6)!;
        Items(menu).Single(i => AutomationProperties.GetAutomationId(i) == "SpellMenu_AddToDictionary").Command!.Execute(null);
        Assert.Equal(["captian"], Added);
        Assert.Equal("The captian glows", tb.Text);
    }

    [AvaloniaFact]
    public void AddToProjectDictionary_CallsTheViewModel()
    {
        var tb = Setup("The captian glows");
        var menu = SpellingContextMenu.CreateMenu(tb, 6)!;
        Items(menu).Single(i => AutomationProperties.GetAutomationId(i) == "SpellMenu_AddToProjectDictionary").Command!.Execute(null);
        Assert.Equal(["captian"], AddedToProject);
    }

    [AvaloniaFact]
    public void NoFinder_NoMenu()
    {
        var tb = Setup("The captian glows");
        SpellingContextMenu.SetFinder(tb, null);
        Assert.Null(SpellingContextMenu.CreateMenu(tb, 6));
    }
}
