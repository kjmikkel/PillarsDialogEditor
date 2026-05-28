using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Import;

namespace DialogEditor.Tests.Views;

public class ImportWarningsDialogTests
{
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
