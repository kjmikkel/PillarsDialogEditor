using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

public class HistoryWindowTests
{
    private const char US = '\u001f';

    public HistoryWindowTests() => Loc.Configure(new StubStringProvider());

    private sealed class FakeGit : IGitRunner
    {
        public Func<string[], GitResult> Handler = _ => new GitResult(0, "", "");
        public GitResult Run(string workingDirectory, params string[] args) => Handler(args);
    }

    private static HistoryViewModel MakeVm(string logStdout)
    {
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var git = new FakeGit
        {
            Handler = a =>
                a is ["rev-parse", "--show-toplevel"] ? new GitResult(0, root + "\n", "") :
                a.Length > 0 && a[0] == "log"         ? new GitResult(0, logStdout, "") :
                                                        new GitResult(0, "", ""),
        };
        var path = Path.Combine(root, $"histwin_{Guid.NewGuid():N}.dialogproject");
        return new HistoryViewModel(git, path);
    }

    [AvaloniaFact]
    public void List_PopulatesFromCommits()
    {
        var vm = MakeVm(string.Join(US, "a1b2c3d", "a1b2c3d4", "Mia", "2026-05-30T10:00:00+00:00", "Tweak"));
        var window = new HistoryWindow(vm);
        window.Show();

        Assert.Equal(1, window.FindControl<ListBox>("CommitList")!.ItemCount);
    }

    [AvaloniaFact]
    public void CompareButton_DisabledUntilSelected()
    {
        var vm = MakeVm(string.Join(US, "a1b2c3d", "a1b2c3d4", "Mia", "2026-05-30T10:00:00+00:00", "Tweak"));
        var window = new HistoryWindow(vm);
        window.Show();

        var btn = window.FindControl<Button>("CompareButton")!;
        Assert.False(btn.Command!.CanExecute(null));

        vm.Selected = vm.Commits[0];
        Assert.True(btn.Command!.CanExecute(null));
    }
}
