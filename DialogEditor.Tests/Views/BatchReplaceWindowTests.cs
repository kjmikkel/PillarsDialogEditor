using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

/// <summary>
/// The conversation header's match count used to be
/// StringFormat='{}{0} match(es)' in the markup. It binds
/// BatchReplaceConversationViewModel.MatchCountLabel now, so these render it for real:
/// a wrong binding path would otherwise blank the count silently. Culture is pinned
/// because the plural category depends on it.
/// </summary>
public class BatchReplaceWindowTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentUICulture;

    public BatchReplaceWindowTests()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        Loc.Configure(new AvaloniaStringProvider());
    }

    public void Dispose() => CultureInfo.CurrentUICulture = _originalCulture;

    private static NodeEditSnapshot Node(int id, string text) =>
        new(id, false, SpeakerCategory.Npc, "", "", text, "",
            "Conversation", "None", "", "", "", false, false, [], [], []);

    private static async Task<BatchReplaceViewModel> PreviewedVm(params string[] nodeTexts)
    {
        var file     = new ConversationFile("conv", @"quests\conv.conversation",
                                            "quests", @"quests\conv.stringtable");
        var provider = new StubProvider(file,
            new ConversationEditSnapshot([.. nodeTexts.Select((t, i) => Node(i + 1, t))]));

        var vm = new BatchReplaceViewModel(
            provider, provider.EnumerateConversations(), isOpenInEditor: _ => false)
        {
            SearchText  = "world",
            ReplaceText = "earth",
        };
        await vm.PreviewCommand.ExecuteAsync(null);
        return vm;
    }

    private static IEnumerable<string> RenderedText(BatchReplaceViewModel vm)
    {
        var window = new BatchReplaceWindow(vm);
        window.Show();
        return window.GetVisualDescendants().OfType<TextBlock>()
                     .Select(t => t.Text ?? "");
    }

    [AvaloniaFact]
    public async Task ConversationHeader_RendersTheSingularMatchCount()
    {
        var vm = await PreviewedVm("Hello world");

        Assert.Single(vm.Results);
        Assert.Contains("1 match", RenderedText(vm));
    }

    [AvaloniaFact]
    public async Task ConversationHeader_RendersThePluralMatchCount()
    {
        var vm = await PreviewedVm("Hello world", "goodbye world", "world again");

        Assert.Contains("3 matches", RenderedText(vm));
    }
}
