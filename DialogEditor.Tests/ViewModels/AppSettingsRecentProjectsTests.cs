using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class AppSettingsRecentProjectsTests : IDisposable
{
    private readonly string _path;

    public AppSettingsRecentProjectsTests()
    {
        _path = Path.GetTempFileName();
        AppSettings.SettingsPathOverride = _path;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { File.Delete(_path); } catch { /* best-effort */ }
    }

    [Fact]
    public void RecentProjects_DefaultsToEmpty()
    {
        File.WriteAllText(_path, "{}");
        Assert.Empty(AppSettings.RecentProjects);
    }

    [Fact]
    public void Add_InsertsAtFront()
    {
        AppSettings.AddRecentProject(@"C:\a\one.dialogproject");
        AppSettings.AddRecentProject(@"C:\a\two.dialogproject");
        Assert.Equal(
            new[] { @"C:\a\two.dialogproject", @"C:\a\one.dialogproject" },
            AppSettings.RecentProjects);
    }

    [Fact]
    public void Add_Duplicate_DifferentCase_MovesToFrontNoDupe()
    {
        AppSettings.AddRecentProject(@"C:\a\one.dialogproject");
        AppSettings.AddRecentProject(@"C:\a\two.dialogproject");
        AppSettings.AddRecentProject(@"C:\A\ONE.DIALOGPROJECT");
        var list = AppSettings.RecentProjects;
        Assert.Equal(2, list.Count);
        Assert.Equal(@"C:\A\ONE.DIALOGPROJECT", list[0]);
        Assert.Equal(@"C:\a\two.dialogproject", list[1]);
    }

    [Fact]
    public void Add_CapsAtTen_EvictsOldest()
    {
        for (var i = 1; i <= 11; i++)
            AppSettings.AddRecentProject($@"C:\a\p{i}.dialogproject");
        var list = AppSettings.RecentProjects;
        Assert.Equal(10, list.Count);
        Assert.Equal(@"C:\a\p11.dialogproject", list[0]);   // newest
        Assert.Equal(@"C:\a\p2.dialogproject", list[9]);    // p1 evicted
        Assert.DoesNotContain(@"C:\a\p1.dialogproject", list);
    }

    [Fact]
    public void Remove_DeletesMatchingEntry()
    {
        AppSettings.AddRecentProject(@"C:\a\one.dialogproject");
        AppSettings.AddRecentProject(@"C:\a\two.dialogproject");
        AppSettings.RemoveRecentProject(@"C:\A\ONE.DIALOGPROJECT");
        Assert.Equal(new[] { @"C:\a\two.dialogproject" }, AppSettings.RecentProjects);
    }

    [Fact]
    public void Clear_EmptiesList()
    {
        AppSettings.AddRecentProject(@"C:\a\one.dialogproject");
        AppSettings.ClearRecentProjects();
        Assert.Empty(AppSettings.RecentProjects);
    }

    [Fact]
    public void RecentProjects_RoundTripsThroughFile()
    {
        AppSettings.AddRecentProject(@"C:\a\one.dialogproject");
        AppSettings.SettingsPathOverride = _path; // re-point (forces fresh Load)
        Assert.Contains(@"C:\a\one.dialogproject", AppSettings.RecentProjects);
    }
}
