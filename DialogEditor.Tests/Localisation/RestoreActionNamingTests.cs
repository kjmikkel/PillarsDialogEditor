using System.Xml.Linq;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// GitHub issue 247: the action that ends a test and puts the game files back (F6) was called
/// "Restore Conversation", "Restore", "Restore (F6)" and, for the separate Restore Backup
/// action, "Restore Full Backup". "Restore Conversation" also suggested it touched one
/// conversation, when it restores every file the test patched. Each action now has one name,
/// and every message that points a Modder at it uses that name.
/// </summary>
public class RestoreActionNamingTests
{
    private static Dictionary<string, string> Strings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var doc = XDocument.Load(Path.Combine(dir!.FullName, "DialogEditor.Avalonia", "Resources", "Strings.axaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return doc.Root!.Elements()
            .Where(e => e.Attribute(x + "Key") is not null)
            .ToDictionary(e => e.Attribute(x + "Key")!.Value, e => e.Value);
    }

    [Theory]
    [InlineData("Restore Conversation")]
    [InlineData("Restore Full Backup")]
    [InlineData("Restore (F6)")]
    [InlineData("press Restore")]
    public void RetiredNameIsNotShown(string retired)
    {
        var uses = Strings().Where(kv => kv.Value.Contains(retired, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key).ToList();
        Assert.True(uses.Count == 0, $"\"{retired}\" is still shown by: {string.Join(", ", uses)}");
    }

    [Fact]
    public void MessagesAboutTheTestUseTheMenuName()
    {
        var strings = Strings();
        var name = strings["TestMode_Restore"];
        foreach (var key in new[] { "TestMode_Message", "Status_FullRestoreTestActive", "Status_TestPatchAlreadyActive" })
            Assert.Contains(name, strings[key]);
    }

    [Fact]
    public void MessagesAboutTheBackupUseTheMenuName()
    {
        var strings = Strings();
        var name = strings["Button_RestoreBackup"].TrimEnd('…');
        foreach (var key in new[]
                 {
                     "Status_RestoreBackupsMissing_One", "Status_RestoreBackupsMissing_Other",
                     "Status_RestoreLeftUnrestored_One", "Status_RestoreLeftUnrestored_Other",
                     "RestoreUnrestored_Message_One", "RestoreUnrestored_Message_Other",
                 })
            Assert.Contains(name, strings[key]);
    }
}
