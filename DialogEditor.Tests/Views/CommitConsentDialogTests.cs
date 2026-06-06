using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.Views;

public class CommitConsentDialogTests
{
    public CommitConsentDialogTests() => Loc.Configure(new StubStringProvider());

    [AvaloniaFact]
    public void ShowsFiles_AndDefaultMessage()
    {
        var dlg = new CommitConsentDialog(new PendingCommit(new[] { "a.dialogproject", "b.json" }, "default msg"));
        dlg.Show();

        var list = dlg.FindControl<ItemsControl>("FileList")!;
        Assert.Equal(2, list.ItemCount);

        var msg = dlg.FindControl<TextBox>("MessageBox")!;
        Assert.Equal("default msg", msg.Text);
    }
}
