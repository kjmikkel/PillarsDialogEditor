using DialogEditor.PatchCli;

namespace DialogEditor.Tests.PatchCli;

/// Issue #77: in the combined patcher zip, dialog-patcher.exe sits in cli\ next to the
/// Patch Manager GUI. A player who double-clicks it must get a note pointing at the GUI
/// and a pause, not a console that flashes help and vanishes. Scripted runs must not change.
public class PatcherCommandLaunchTests
{
    private sealed class FakeLaunch(bool ownsConsole) : IConsoleLaunch
    {
        public bool OwnsConsole { get; } = ownsConsole;
        public int  Waits       { get; private set; }
        public void WaitForKey() => Waits++;
    }

    private static (int Code, string Out, string Err) Run(IConsoleLaunch? launch, params string[] args)
    {
        var o = new StringWriter(); var e = new StringWriter();
        var code = PatcherCommand.Run(args, o, e, launch);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void NoArgs_DoubleClicked_PointsAtPatchManager_AndWaitsForKey()
    {
        var launch = new FakeLaunch(ownsConsole: true);

        var (code, output, err) = Run(launch);

        Assert.Equal(0, code);
        Assert.Contains("DialogEditor.PatchManager.exe", output);
        Assert.Contains("Usage:", output);
        Assert.Equal("", err);
        Assert.Equal(1, launch.Waits);
    }

    [Fact]
    public void NoArgs_FromTerminalOrScript_KeepsMissingArgumentError()
    {
        var launch = new FakeLaunch(ownsConsole: false);

        var (code, output, err) = Run(launch);

        Assert.Equal(2, code);
        Assert.Contains("Missing required arguments", err);
        Assert.DoesNotContain("DialogEditor.PatchManager.exe", output + err);
        Assert.Equal(0, launch.Waits);
    }

    [Fact]
    public void NoArgs_WithoutLaunchInfo_KeepsMissingArgumentError()
    {
        var (code, _, err) = Run(launch: null);

        Assert.Equal(2, code);
        Assert.Contains("Missing required arguments", err);
    }

    [Fact]
    public void WithArgs_DoubleClickedConsole_NeverWaits()
    {
        // Dragging a file onto the exe also gives it its own console, but that is a real
        // (if incomplete) invocation — report the error as usual, don't reinterpret it.
        var launch = new FakeLaunch(ownsConsole: true);

        var (code, _, _) = Run(launch, "--version");

        Assert.Equal(0, code);
        Assert.Equal(0, launch.Waits);
    }
}
