using DialogEditor.Core.Diagnostics;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

/// Issue 270: privacy mode state, launch-arg parsing and path masking.
public class PrivacyModeTests : IDisposable
{
    public PrivacyModeTests()
    {
        AppSettings.SettingsPathOverride = Path.GetTempFileName();
        PrivacyMode.ResetForTests();
    }

    public void Dispose()
    {
        PrivacyMode.ResetForTests();
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null) File.Delete(path);
    }

    [Fact]
    public void AppSettings_PrivacyMode_DefaultsFalse_AndRoundTrips()
    {
        Assert.False(AppSettings.PrivacyMode);
        AppSettings.PrivacyMode = true;
        Assert.True(AppSettings.PrivacyMode);
    }

    [Fact]
    public void IsOn_DefaultsFalse() => Assert.False(PrivacyMode.IsOn);

    [Fact]
    public void SetPersisted_WritesSettings_AndRaisesChanged()
    {
        var raised = 0;
        void Handler(object? s, EventArgs e) => raised++;
        PrivacyMode.Changed += Handler;
        try { PrivacyMode.SetPersisted(true); }
        finally { PrivacyMode.Changed -= Handler; }

        Assert.True(PrivacyMode.IsOn);
        Assert.True(AppSettings.PrivacyMode);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ForceOnForSession_TurnsOn_WithoutSaving()
    {
        PrivacyMode.ForceOnForSession();
        Assert.True(PrivacyMode.IsOn);
        Assert.False(AppSettings.PrivacyMode);
    }

    [Fact]
    public void SetPersistedFalse_WhileForced_StaysOn()
    {
        PrivacyMode.ForceOnForSession();
        PrivacyMode.SetPersisted(false);
        Assert.True(PrivacyMode.IsOn);
    }

    [Theory]
    [InlineData("--privacy-mode", true)]
    [InlineData("--PRIVACY-MODE", true)]
    [InlineData("--other", false)]
    public void LaunchArgs_HasPrivacyMode(string arg, bool expected)
        => Assert.Equal(expected, LaunchArgs.HasPrivacyMode(["x", arg]));

    [Fact]
    public void LaunchArgs_NullOrEmpty_IsFalse()
    {
        Assert.False(LaunchArgs.HasPrivacyMode(null));
        Assert.False(LaunchArgs.HasPrivacyMode([]));
    }

    // A fictional profile, so the privacy guard (issue 271) may let it through.
    private const string FakeProfile = @"C:\Users\alice"; // privacy-guard: allow

    private static CrashReportScrubber Fake() => new(
        [new ScrubRoot(FakeProfile, "%USERPROFILE%")],
        [new ScrubWord("alice", "%USERNAME%")]);

    [Fact]
    public void ForDisplay_Off_ReturnsUnchanged()
        => Assert.Equal($@"{FakeProfile}\x", PathDisplay.ForDisplay($@"{FakeProfile}\x", Fake(), false));

    [Fact]
    public void ForDisplay_On_ScrubsProfilePath()
        => Assert.Equal(@"%USERPROFILE%\Games\Deadfire",
            PathDisplay.ForDisplay($@"{FakeProfile}\Games\Deadfire", Fake(), true));

    [Fact]
    public void ForDisplay_On_AbsolutePathOutsideRoots_ShowsLastSegmentOnly()
        => Assert.Equal(@"…\Pillars of Eternity II Deadfire",
            PathDisplay.ForDisplay(@"D:\GOG Games\Pillars of Eternity II Deadfire", Fake(), true));

    [Fact]
    public void ForDisplay_On_KeepsTrailingSeparator()
        => Assert.Equal(@"…\Deadfire\",
            PathDisplay.ForDisplay(@"D:\GOG Games\Deadfire\", Fake(), true));

    [Fact]
    public void ForDisplay_On_NullAndEmpty_Unchanged()
    {
        Assert.Null(PathDisplay.ForDisplay(null, Fake(), true));
        Assert.Equal("", PathDisplay.ForDisplay("", Fake(), true));
    }

    [Fact]
    public void ForDisplay_On_RelativeOrNonPath_Unchanged()
        => Assert.Equal("hello", PathDisplay.ForDisplay("hello", Fake(), true));

    [Fact]
    public void ForDisplay_UsesGlobalState()
    {
        Assert.Equal(@"D:\a\b", PathDisplay.ForDisplay(@"D:\a\b"));
        PrivacyMode.ForceOnForSession();
        Assert.Equal(@"…\b", PathDisplay.ForDisplay(@"D:\a\b"));
    }
}

public class PrivacyModeWeakSubscribeTests : IDisposable
{
    public PrivacyModeWeakSubscribeTests()
    {
        AppSettings.SettingsPathOverride = Path.GetTempFileName();
        PrivacyMode.ResetForTests();
    }

    public void Dispose()
    {
        PrivacyMode.ResetForTests();
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null) File.Delete(path);
    }

    private sealed class Owner { public int Calls; }

    [Fact]
    public void WeakSubscribe_InvokesWithOwner_WhenChanged()
    {
        var owner = new Owner();
        PrivacyMode.WeakSubscribe(owner, o => o.Calls++);
        PrivacyMode.SetPersisted(true);
        Assert.Equal(1, owner.Calls);
        GC.KeepAlive(owner);
    }

    [Fact]
    public void WeakSubscribe_DoesNotRootTheOwner()
    {
        var weak = Subscribe();
        for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Assert.False(weak.TryGetTarget(out _));
        PrivacyMode.SetPersisted(true);   // must not throw on the dead subscription

        static WeakReference<Owner> Subscribe()
        {
            var o = new Owner();
            PrivacyMode.WeakSubscribe(o, x => x.Calls++);
            return new WeakReference<Owner>(o);
        }
    }
}
