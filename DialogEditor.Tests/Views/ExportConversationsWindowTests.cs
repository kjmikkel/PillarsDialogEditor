using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

public class ExportConversationsWindowTests
{
    public ExportConversationsWindowTests() =>
        Loc.Configure(new StubStringProvider());

    private static ExportConversationsViewModel MakeVm(string? currentConversation = null) =>
        new(
            ["conv_a", "conv_b"],
            currentConversation,
            _ => [],
            new StubFilePicker(),
            new StubFolderPicker());

    [AvaloniaFact]
    public void ArticyRadioButton_IsDisabled()
    {
        var vm     = MakeVm();
        var window = new ExportConversationsWindow(vm);
        window.Show();
        Assert.False(window.FindControl<RadioButton>("ArticyRadioButton")!.IsEnabled);
    }

    [AvaloniaFact]
    public void ExportButton_IsDisabled_WhenNothingChecked()
    {
        var vm     = MakeVm(currentConversation: null);
        var window = new ExportConversationsWindow(vm);
        window.Show();
        Assert.False(window.FindControl<Button>("ExportButton")!.Command!.CanExecute(null));
    }

    [AvaloniaFact]
    public void ExportButton_Enables_AfterCheckingItem()
    {
        var vm     = MakeVm(currentConversation: null);
        var window = new ExportConversationsWindow(vm);
        window.Show();
        vm.ConversationItems[0].IsChecked = true;
        Assert.True(window.FindControl<Button>("ExportButton")!.IsEnabled);
    }

    [AvaloniaFact]
    public void SelectAllButton_ChecksAllItems()
    {
        var vm     = MakeVm(currentConversation: null);
        var window = new ExportConversationsWindow(vm);
        window.Show();
        window.FindControl<Button>("SelectAllButton")!.Command!.Execute(null);
        Assert.All(vm.ConversationItems, i => Assert.True(i.IsChecked));
    }

    [AvaloniaFact]
    public void SelectNoneButton_UnchecksAllItems()
    {
        var vm     = MakeVm(currentConversation: "conv_a");
        var window = new ExportConversationsWindow(vm);
        window.Show();
        window.FindControl<Button>("SelectNoneButton")!.Command!.Execute(null);
        Assert.All(vm.ConversationItems, i => Assert.False(i.IsChecked));
    }
}
