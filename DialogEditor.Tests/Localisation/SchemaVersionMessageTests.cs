using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Patch.Schema;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Localisation;

public class SchemaVersionMessageTests
{
    [Fact]
    public void TopLevel_UsesTheTopLevelTemplate()
    {
        Loc.Configure(new StubStringProvider());
        var text = SchemaVersionMessages.TooNew(new UnsupportedSchemaVersionException(SchemaFileKind.Project, 2, 1), "mod.dialogproject");
        Assert.StartsWith("Schema_TooNew", text);
        Assert.DoesNotContain("Schema_TooNewInConversation", text);
    }

    [Fact]
    public void Nested_UsesTheConversationTemplate()
    {
        Loc.Configure(new StubStringProvider());
        var text = SchemaVersionMessages.TooNew(
            new UnsupportedSchemaVersionException(SchemaFileKind.ConversationPatch, 3, 2, "greeting"), "mod.dialogproject");
        Assert.StartsWith("Schema_TooNewInConversation", text);
    }

    [Fact]
    public void ForPatcher_TopLevel_UsesThePatcherTemplate()
    {
        Loc.Configure(new StubStringProvider());
        var text = SchemaVersionMessages.TooNewForPatcher(new UnsupportedSchemaVersionException(SchemaFileKind.Project, 2, 1), "mod.dialogpack");
        Assert.StartsWith("Schema_TooNewForPatcher", text);
        Assert.DoesNotContain("InConversation", text);
    }

    [Fact]
    public void ForPatcher_Nested_UsesThePatcherConversationTemplate()
    {
        Loc.Configure(new StubStringProvider());
        var text = SchemaVersionMessages.TooNewForPatcher(
            new UnsupportedSchemaVersionException(SchemaFileKind.ConversationPatch, 3, 2, "greeting"), "mod.dialogpack");
        Assert.StartsWith("Schema_TooNewForPatcherInConversation", text);
    }
}

public class SchemaVersionMessageResourceEndToEndTests
{
    public SchemaVersionMessageResourceEndToEndTests() => Loc.Configure(new AvaloniaStringProvider());

    [AvaloniaFact]
    public void TopLevel_RendersFromSharedStrings()
        => Assert.Equal(
            "'mod.dialogproject' was saved by a newer version of Pillars Dialog Editor (project format 2). " +
            "This version reads up to project format 1. Update to the latest version to open it.",
            SchemaVersionMessages.TooNew(new UnsupportedSchemaVersionException(SchemaFileKind.Project, 2, 1), "mod.dialogproject"));

    [AvaloniaFact]
    public void Nested_RendersTheConversationName()
        => Assert.Contains("'greeting'",
            SchemaVersionMessages.TooNew(
                new UnsupportedSchemaVersionException(SchemaFileKind.ConversationPatch, 3, 2, "greeting"), "mod.dialogpack"));

    [AvaloniaFact]
    public void EveryKind_HasAFormatName()
    {
        foreach (var kind in Enum.GetValues<SchemaFileKind>())
            Assert.DoesNotContain("Schema_Format_",
                SchemaVersionMessages.TooNew(new UnsupportedSchemaVersionException(kind, 9, 1), "f"));
    }

    [AvaloniaFact]
    public void ForPatcher_RendersFromSharedStrings()
        => Assert.Equal(
            "'mod.dialogpack' needs a newer Pillars Dialog Patcher (project format 2). " +
            "This version reads up to project format 1. Nothing was changed.",
            SchemaVersionMessages.TooNewForPatcher(new UnsupportedSchemaVersionException(SchemaFileKind.Project, 2, 1), "mod.dialogpack"));

    [AvaloniaFact]
    public void ForPatcher_Nested_RendersTheConversationName()
        => Assert.Contains("'greeting'",
            SchemaVersionMessages.TooNewForPatcher(
                new UnsupportedSchemaVersionException(SchemaFileKind.ConversationPatch, 3, 2, "greeting"), "mod.dialogpack"));

    [AvaloniaFact]
    public void Title_RendersFromSharedStrings()
        => Assert.Equal("Newer file format", SchemaVersionMessages.Title);
}
