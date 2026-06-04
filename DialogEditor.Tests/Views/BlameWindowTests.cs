using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

public class BlameWindowTests
{
    public BlameWindowTests() => Loc.Configure(new StubStringProvider());

    private sealed class FakeGit : IGitRunner
    {
        public Func<string[], GitResult> Handler = _ => new GitResult(0, "", "");
        public GitResult Run(string workingDirectory, params string[] args) => Handler(args);
    }

    private static string OneNodePorcelain()
    {
        string[] content =
        [
            "{", "  \"Patches\": {", "    \"greeting\": {", "      \"AddedNodes\": [",
            "        {", "          \"NodeId\": 1", "        }", "      ]", "    }", "  }", "}",
        ];
        var sb = new StringBuilder();
        for (var i = 0; i < content.Length; i++)
        {
            var final = i + 1;
            sb.Append($"a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2 {final} {final} 1\n");
            sb.Append("author Mia\nauthor-mail <m@x>\nauthor-time 1700000000\nauthor-tz +0000\n");
            sb.Append("committer Mia\ncommitter-mail <m@x>\ncommitter-time 1700000000\ncommitter-tz +0000\n");
            sb.Append("summary Add greeting\nfilename test.dialogproject\n");
            sb.Append($"\t{content[i]}\n");
        }
        return sb.ToString();
    }

    private static BlameViewModel MakeVm()
    {
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var git = new FakeGit
        {
            Handler = a =>
                a is ["rev-parse", "--show-toplevel"] ? new GitResult(0, root + "\n", "") :
                a.Length > 0 && a[0] == "blame"       ? new GitResult(0, OneNodePorcelain(), "") :
                                                        new GitResult(0, "", ""),
        };
        var path = Path.Combine(root, $"blamewin_{Guid.NewGuid():N}.dialogproject");
        return new BlameViewModel(git, path);
    }

    [AvaloniaFact]
    public void List_PopulatesFromAttribution()
    {
        var window = new BlameWindow(MakeVm());
        window.Show();

        Assert.Equal(1, window.FindControl<ListBox>("BlameList")!.ItemCount);
    }
}
