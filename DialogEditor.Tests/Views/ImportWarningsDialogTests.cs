using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Import;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

public class ImportWarningsDialogTests
{
    // The dialog resolves localized strings at construction; configure Loc so these tests
    // don't depend on another test having done so first (xUnit ordering is nondeterministic).
    public ImportWarningsDialogTests() => Loc.Configure(new StubStringProvider());

    private static readonly IReadOnlyList<ImportWarning> Warnings =
    [
        new("if", 2),
        new("set", 1),
    ];

    [AvaloniaFact]
    public void Dialog_ListsOneRowPerWarning()
    {
        var dialog = new ImportWarningsDialog(Warnings);
        dialog.Show();
        Assert.Equal(Warnings.Count, dialog.FindControl<ItemsControl>("WarningsList")!.ItemCount);
    }

    [AvaloniaFact]
    public void OkButton_ClosesDialog()
    {
        var dialog = new ImportWarningsDialog(Warnings);
        dialog.Show();
        dialog.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(dialog.IsVisible);
    }

    [AvaloniaFact]
    public void EscapeKey_ClosesDialog()
    {
        var dialog = new ImportWarningsDialog(Warnings);
        dialog.Show();
        dialog.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Escape,
        });
        Assert.False(dialog.IsVisible);
    }
}
