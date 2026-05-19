using System.IO;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class PatchListSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesEntriesAndGameFolder()
    {
        var list = new PatchList(
            PatchList.CurrentSchemaVersion,
            @"C:\Games\PoE1",
            [new PatchListEntry("../mod.dialogproject", @"C:\mods\mod.dialogproject")]);

        var dir  = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var path = Path.Combine(dir, "test.patchlist");
        try
        {
            PatchListSerializer.SaveToFile(path, list);
            var loaded = PatchListSerializer.LoadFromFile(path);

            Assert.Equal(list.GameFolder,          loaded.GameFolder);
            Assert.Single(loaded.Entries);
            Assert.Equal(list.Entries[0].RelativePath, loaded.Entries[0].RelativePath);
            Assert.Equal(list.Entries[0].AbsolutePath, loaded.Entries[0].AbsolutePath);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ResolvePath_RelativeExists_ReturnsRelative()
    {
        var dir          = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var patchlistDir = Path.Combine(dir, "list");
        var modDir       = Path.Combine(dir, "mod");
        try
        {
            Directory.CreateDirectory(patchlistDir);
            Directory.CreateDirectory(modDir);

            var modFile      = Path.Combine(modDir, "my.dialogproject");
            File.WriteAllText(modFile, "{}");

            var patchlistPath = Path.Combine(patchlistDir, "order.patchlist");
            var relative      = Path.GetRelativePath(patchlistDir, modFile);
            var entry         = new PatchListEntry(relative, modFile);

            var resolved = PatchListSerializer.ResolvePath(patchlistPath, entry);
            Assert.Equal(Path.GetFullPath(modFile), Path.GetFullPath(resolved));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ResolvePath_RelativeMissing_FallsBackToAbsolute()
    {
        var entry    = new PatchListEntry("nonexistent/relative.dialogproject",
                                          @"C:\absolute\path.dialogproject");
        var resolved = PatchListSerializer.ResolvePath(@"C:\some\list.patchlist", entry);
        Assert.Equal(@"C:\absolute\path.dialogproject", resolved);
    }
}

public class ConflictDetectorTests
{
    private static ConversationPatch MakePatch(string conv, int nodeId, string fieldName, string from, string to)
    {
        var mod = new NodeModification(
            nodeId,
            new Dictionary<string, FieldChange> { [fieldName] = new FieldChange(from, to) },
            [], []);
        return new ConversationPatch(conv, 1, [], [], [mod]);
    }

    [Fact]
    public void Detect_TwoPatchesModifySameField_ReportsConflict()
    {
        var projects = new[]
        {
            ("ModA", new Dictionary<string, ConversationPatch> { ["conv1"] = MakePatch("conv1", 5, "DefaultText", "Hello", "Hi") } as IReadOnlyDictionary<string, ConversationPatch>),
            ("ModB", new Dictionary<string, ConversationPatch> { ["conv1"] = MakePatch("conv1", 5, "DefaultText", "Hello", "Hey") } as IReadOnlyDictionary<string, ConversationPatch>),
        };

        var conflicts = ConflictDetector.Detect(projects);

        Assert.Single(conflicts);
        Assert.Equal("conv1",       conflicts[0].ConversationName);
        Assert.Equal(5,             conflicts[0].NodeId);
        Assert.Equal("DefaultText", conflicts[0].FieldName);
        Assert.Equal(0,             conflicts[0].FirstPatchIndex);
        Assert.Equal(1,             conflicts[0].SecondPatchIndex);
    }

    [Fact]
    public void Detect_NoDuplicateFields_ReturnsEmpty()
    {
        var projects = new[]
        {
            ("ModA", new Dictionary<string, ConversationPatch> { ["conv1"] = MakePatch("conv1", 5, "DefaultText", "Hello", "Hi") } as IReadOnlyDictionary<string, ConversationPatch>),
            ("ModB", new Dictionary<string, ConversationPatch> { ["conv1"] = MakePatch("conv1", 5, "FemaleText",  "",      "Hi") } as IReadOnlyDictionary<string, ConversationPatch>),
        };

        Assert.Empty(ConflictDetector.Detect(projects));
    }

    [Fact]
    public void Detect_DeleteVsModify_ReportsConflict()
    {
        var modifyPatch = MakePatch("conv1", 7, "DefaultText", "Old", "New");
        var deletePatch = new ConversationPatch("conv1", 1, [], [7], []);

        var projects = new[]
        {
            ("ModA", new Dictionary<string, ConversationPatch> { ["conv1"] = modifyPatch } as IReadOnlyDictionary<string, ConversationPatch>),
            ("ModB", new Dictionary<string, ConversationPatch> { ["conv1"] = deletePatch } as IReadOnlyDictionary<string, ConversationPatch>),
        };

        var conflicts = ConflictDetector.Detect(projects);
        Assert.NotEmpty(conflicts);
    }
}

public class PatchMergerTests
{
    private static NodeEditSnapshot MakeNode(int id, string text = "text") =>
        new(id, false, SpeakerCategory.Npc, "", "", text, "", "Conversation", "None",
            "", "", "", false, false, [], [], []);

    [Fact]
    public void Merge_LaterAddedNodeReplacesEarlier()
    {
        var patch1 = new ConversationPatch("conv", 1, [MakeNode(10, "v1")], [], []);
        var patch2 = new ConversationPatch("conv", 1, [MakeNode(10, "v2")], [], []);

        var merged = PatchMerger.Merge("conv", [patch1, patch2]);

        Assert.Single(merged.AddedNodes);
        Assert.Equal("v2", merged.AddedNodes[0].DefaultText);
    }

    [Fact]
    public void Merge_DeletedNodeIdsUnioned()
    {
        var patch1 = new ConversationPatch("conv", 1, [], [1, 2], []);
        var patch2 = new ConversationPatch("conv", 1, [], [2, 3], []);

        var merged = PatchMerger.Merge("conv", [patch1, patch2]);

        Assert.Equal(3, merged.DeletedNodeIds.Distinct().Count());
        Assert.Contains(1, merged.DeletedNodeIds);
        Assert.Contains(3, merged.DeletedNodeIds);
    }

    [Fact]
    public void Merge_LaterModificationWinsOnSameField()
    {
        var mod1 = new NodeModification(5,
            new Dictionary<string, FieldChange> { ["DefaultText"] = new("old", "v1") },
            [], []);
        var mod2 = new NodeModification(5,
            new Dictionary<string, FieldChange> { ["DefaultText"] = new("old", "v2") },
            [], []);

        var patch1 = new ConversationPatch("conv", 1, [], [], [mod1]);
        var patch2 = new ConversationPatch("conv", 1, [], [], [mod2]);

        var merged = PatchMerger.Merge("conv", [patch1, patch2]);

        Assert.Single(merged.ModifiedNodes);
        Assert.Equal("v2", merged.ModifiedNodes[0].FieldChanges["DefaultText"].To);
    }

    [Fact]
    public void Merge_FieldsFromDifferentPatchesCombined()
    {
        var mod1 = new NodeModification(5,
            new Dictionary<string, FieldChange> { ["DefaultText"] = new("old", "hi") },
            [], []);
        var mod2 = new NodeModification(5,
            new Dictionary<string, FieldChange> { ["FemaleText"] = new("", "her") },
            [], []);

        var patch1 = new ConversationPatch("conv", 1, [], [], [mod1]);
        var patch2 = new ConversationPatch("conv", 1, [], [], [mod2]);

        var merged = PatchMerger.Merge("conv", [patch1, patch2]);

        Assert.Single(merged.ModifiedNodes);
        Assert.Equal("hi",  merged.ModifiedNodes[0].FieldChanges["DefaultText"].To);
        Assert.Equal("her", merged.ModifiedNodes[0].FieldChanges["FemaleText"].To);
    }
}
