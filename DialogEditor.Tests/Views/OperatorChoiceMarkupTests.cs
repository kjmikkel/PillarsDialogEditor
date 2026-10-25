using System.Xml.Linq;

namespace DialogEditor.Tests.Views;

/// <summary>
/// GitHub issue 266: in both editors an Operator parameter is a fixed list (a ComboBox of
/// plain labels), and only the other enums keep the free-text AutoCompleteBox.
/// </summary>
public class OperatorChoiceMarkupTests
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
    [InlineData("ConditionEditorWindow")]
    [InlineData("ScriptEditorWindow")]
    public void OperatorIsAFixedList_OtherEnumsStayFreeText(string window)
    {
        var doc = Load(window);
        Assert.Contains(doc.Descendants(), e => e.Name.LocalName == "ComboBox"
            && e.Attribute("IsVisible")?.Value == "{Binding IsFixedChoice}"
            && e.Attribute("ItemsSource")?.Value == "{Binding EnumOptions}"
            && (e.Attribute("SelectedItem")?.Value ?? "").StartsWith("{Binding Value"));
        Assert.DoesNotContain(doc.Descendants(), e => e.Attribute("IsVisible")?.Value == "{Binding IsEnum}");
        Assert.Contains(doc.Descendants(), e => e.Name.LocalName == "AutoCompleteBox"
            && e.Attribute("IsVisible")?.Value == "{Binding IsFreeEnum}");
    }
}
