using System.Xml.Linq;

namespace DialogEditor.Tests.Views;

/// <summary>
/// GitHub issue 254: the five tool windows whose lists sat blank with no reason now put the
/// shared EmptyState control over each list, bound to the view model's explanation.
/// </summary>
public class EmptyStateUsageTests
{
    private static XDocument Load(string window)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return XDocument.Load(Path.Combine(dir!.FullName, "DialogEditor.Avalonia", "Views", window + ".axaml"));
    }

    [Theory]
    [InlineData("FindInProjectWindow", "EmptyMessage")]
    [InlineData("BatchReplaceWindow", "EmptyMessage")]
    [InlineData("VoAliasPickerWindow", "EmptyMessage")]
    [InlineData("DiffWindow", "EmptyMessage")]
    [InlineData("RepDispositionBalanceWindow", "DispositionEmptyMessage")]
    [InlineData("RepDispositionBalanceWindow", "ReputationEmptyMessage")]
    public void WindowShowsWhyItsListIsEmpty(string window, string messageProperty)
    {
        var states = Load(window).Descendants().Where(e => e.Name.LocalName == "EmptyState").ToList();
        var state = states.SingleOrDefault(e => e.Attribute("Message")?.Value == $"{{Binding {messageProperty}}}");
        Assert.True(state is not null, $"{window} has no <EmptyState Message=\"{{Binding {messageProperty}}}\">");
        Assert.Contains("{Binding", state!.Attribute("Hint")?.Value ?? "");
    }

    [Fact]
    public void CompareVersions_SelectHint_ShowsOnlyWhenTheListHasSomething()
    {
        var hint = Load("DiffWindow").Descendants()
            .Single(e => e.Attribute("Text")?.Value == "{DynamicResource DiffWindow_SelectHint}");
        Assert.Equal("{Binding ShowSelectHint}", hint.Attribute("IsVisible")?.Value);
    }
}
