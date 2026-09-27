using DialogEditor.Core.Diagnostics;

namespace DialogEditor.Tests.Diagnostics;

/// <summary>
/// Issue #72: the pre-filled crash report lands on a PUBLIC issue tracker, so everything
/// that identifies the reporter's machine must be gone before the URL is built. These tests
/// use a synthetic profile ("C:\Users\Alice Smith") so they are deterministic on any runner;
/// one test at the bottom checks the real environment.
/// </summary>
public class CrashReportScrubberTests
{
    private const string Profile      = @"C:\Users\Alice Smith";
    private const string LocalAppData = @"C:\Users\Alice Smith\AppData\Local";
    private const string AppData      = @"C:\Users\Alice Smith\AppData\Roaming";

    private static CrashReportScrubber Make() => new(
        roots:
        [
            new ScrubRoot(Profile,      "%USERPROFILE%"),
            new ScrubRoot(LocalAppData, "%LOCALAPPDATA%"),
            new ScrubRoot(AppData,      "%APPDATA%"),
        ],
        words:
        [
            new ScrubWord("alicesmith", "%USERNAME%"),
            new ScrubWord("DESKTOP-ALICE7", "%COMPUTERNAME%"),
        ]);

    [Fact]
    public void Scrub_ReplacesProfilePathAndKeepsTheRelativeRemainder()
    {
        var result = Make().Scrub(@"Could not open C:\Users\Alice Smith\Documents\mod\a.conversation");

        Assert.Equal(@"Could not open %USERPROFILE%\Documents\mod\a.conversation", result);
    }

    [Fact]
    public void Scrub_ReplacesProfilePath_WithForwardSlashes()
    {
        var result = Make().Scrub("at file:///C:/Users/Alice Smith/Documents/x.cs:line 4");

        Assert.Equal("at file:///%USERPROFILE%/Documents/x.cs:line 4", result);
    }

    [Fact]
    public void Scrub_ReplacesProfilePath_CaseInsensitively()
    {
        var result = Make().Scrub(@"c:\users\ALICE SMITH\Desktop");

        Assert.Equal(@"%USERPROFILE%\Desktop", result);
    }

    [Fact]
    public void Scrub_ReplacesProfilePath_WithDoubledBackslashesFromEscapedJson()
    {
        var result = Make().Scrub(@"{""path"":""C:\\Users\\Alice Smith\\x.json""}");

        Assert.Equal(@"{""path"":""%USERPROFILE%\\x.json""}", result);
    }

    [Fact]
    public void Scrub_ReplacesBareProfilePath_AtEndOfText()
    {
        Assert.Equal("home is %USERPROFILE%", Make().Scrub(@"home is C:\Users\Alice Smith"));
    }

    [Fact]
    public void Scrub_PrefersTheMostSpecificRoot()
    {
        // %LOCALAPPDATA% is under the profile; the longer, more specific root must win so
        // the report says where the log lives without saying whose machine it is.
        var result = Make().Scrub(@"log: C:\Users\Alice Smith\AppData\Local\PillarsDialogEditor\app.log");

        Assert.Equal(@"log: %LOCALAPPDATA%\PillarsDialogEditor\app.log", result);
    }

    [Fact]
    public void Scrub_ReplacesEveryOccurrence()
    {
        var result = Make().Scrub(@"C:\Users\Alice Smith\a and C:\Users\Alice Smith\AppData\Roaming\b");

        Assert.Equal(@"%USERPROFILE%\a and %APPDATA%\b", result);
    }

    [Fact]
    public void Scrub_DoesNotReplaceASiblingFolderThatMerelySharesAPrefix()
    {
        // "C:\Users\Alice Smithers" is a different profile: replacing the prefix would
        // leave "%USERPROFILE%ers", which is wrong and still half-identifying.
        var result = Make().Scrub(@"C:\Users\Alice Smithers\x");

        Assert.DoesNotContain("%USERPROFILE%", result);
    }

    [Fact]
    public void Scrub_ReplacesBareUserName_AsAWholeWord_CaseInsensitively()
    {
        var result = Make().Scrub("Access denied for AliceSmith on share \\\\nas\\alicesmith$");

        Assert.Equal("Access denied for %USERNAME% on share \\\\nas\\%USERNAME%$", result);
    }

    [Fact]
    public void Scrub_DoesNotReplaceUserNameInsideALongerWord()
    {
        var scrubber = new CrashReportScrubber([], [new ScrubWord("dev", "%USERNAME%")]);

        Assert.Equal("DeveloperTools failed for %USERNAME%",
            scrubber.Scrub("DeveloperTools failed for dev"));
    }

    [Fact]
    public void Scrub_ReplacesMachineName()
    {
        var result = Make().Scrub(@"\\DESKTOP-ALICE7\share unreachable");

        Assert.Equal(@"\\%COMPUTERNAME%\share unreachable", result);
    }

    [Fact]
    public void Scrub_IgnoresWordsTooShortToScrubSafely()
    {
        // A one- or two-letter account name would shred ordinary text ("a", "at") while
        // adding no privacy the path rules don't already give.
        var scrubber = new CrashReportScrubber([], [new ScrubWord("at", "%USERNAME%")]);

        Assert.Equal("   at Foo.Bar()", scrubber.Scrub("   at Foo.Bar()"));
    }

    [Fact]
    public void Scrub_IgnoresRootsThatAreJustADriveOrFilesystemRoot()
    {
        // A misconfigured TEMP=C:\ must not turn every path in the trace into %TEMP%.
        var scrubber = new CrashReportScrubber(
            [new ScrubRoot(@"C:\", "%TEMP%"), new ScrubRoot("/", "%TEMP%")], []);

        Assert.Equal(@"D:\Games\PoE and C:\Program Files\x /usr/lib",
            scrubber.Scrub(@"D:\Games\PoE and C:\Program Files\x /usr/lib"));
    }

    [Fact]
    public void Scrub_HandlesUnixHomePaths()
    {
        var scrubber = new CrashReportScrubber([new ScrubRoot("/home/alice/", "%USERPROFILE%")], []);

        Assert.Equal("open %USERPROFILE%/.config/x failed",
            scrubber.Scrub("open /home/alice/.config/x failed"));
    }

    [Fact]
    public void Scrub_LeavesTextWithoutPersonalDataUntouched()
    {
        const string text = @"   at DialogEditor.Core.Parsing.Parser.Read() in D:\a\PillarsDialogEditor\Parser.cs:line 12";

        Assert.Equal(text, Make().Scrub(text));
    }

    [Fact]
    public void Scrub_EmptyText_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, Make().Scrub(string.Empty));
    }

    [Fact]
    public void FromEnvironment_RemovesTheRealProfilePathAndUserName()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local   = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var text    = $"a {Path.Combine(profile, "Documents", "x.txt")} b {Path.Combine(local, "y.log")} c {Environment.UserName} d {Environment.MachineName}";

        var result = CrashReportScrubber.FromEnvironment().Scrub(text);

        Assert.DoesNotContain(profile, result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(local,   result, StringComparison.OrdinalIgnoreCase);
        if (Environment.UserName.Length >= CrashReportScrubber.MinWordLength)
            Assert.DoesNotContain(Environment.UserName, result, StringComparison.OrdinalIgnoreCase);
        if (Environment.MachineName.Length >= CrashReportScrubber.MinWordLength)
            Assert.DoesNotContain(Environment.MachineName, result, StringComparison.OrdinalIgnoreCase);
    }
}
