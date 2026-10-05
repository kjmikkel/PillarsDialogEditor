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

    [Fact]
    public void Detect_DeleteVsModify_CarriesNoFieldNameAndIsFlaggedAsADeletion()
    {
        // The row used to carry the literal "(deleted)" in FieldName, which was both the
        // text the Patch Manager showed and part of the DistinctBy identity key — so it
        // could not be localised without making identity language-dependent. The kind is
        // now data and PatchConflictRowViewModel renders the label.
        var projects = new[]
        {
            ("ModA", new Dictionary<string, ConversationPatch> { ["conv1"] = MakePatch("conv1", 7, "DefaultText", "Old", "New") } as IReadOnlyDictionary<string, ConversationPatch>),
            ("ModB", new Dictionary<string, ConversationPatch> { ["conv1"] = new ConversationPatch("conv1", 1, [], [7], []) } as IReadOnlyDictionary<string, ConversationPatch>),
        };

        var c = Assert.Single(ConflictDetector.Detect(projects));

        Assert.Null(c.FieldName);
        Assert.True(c.IsDeletion);
    }

    [Fact]
    public void Detect_FieldConflict_IsNotFlaggedAsADeletion()
    {
        var projects = new[]
        {
            ("ModA", new Dictionary<string, ConversationPatch> { ["conv1"] = MakePatch("conv1", 5, "DefaultText", "Hello", "Hi") } as IReadOnlyDictionary<string, ConversationPatch>),
            ("ModB", new Dictionary<string, ConversationPatch> { ["conv1"] = MakePatch("conv1", 5, "DefaultText", "Hello", "Hey") } as IReadOnlyDictionary<string, ConversationPatch>),
        };

        Assert.False(Assert.Single(ConflictDetector.Detect(projects)).IsDeletion);
    }

    // ── Issue #6: clashes the detector used to miss ─────────────────────────────
    // PatchMerger resolves each of these last-wins, so before the fix the earlier pack's
    // version vanished with no warning at all.

    private static NodeEditSnapshot AddedNode(int id, string text) =>
        new(id, false, SpeakerCategory.Npc, "", "", text, "", "Conversation", "None",
            "", "", "", false, false, [], [], []);

    private static NodeModification LinkEdit(int fromNodeId, int toNodeId,
                                             bool added = false, int weight = 1) =>
        new(fromNodeId, new Dictionary<string, FieldChange>(),
            added ? [new LinkEditSnapshot(fromNodeId, toNodeId, weight, "", false)] : [],
            [],
            added ? [] : [new ModifiedLink(toNodeId, weight, "")]);

    private static (string, IReadOnlyDictionary<string, ConversationPatch>) Project(
        string name, ConversationPatch patch) =>
        (name, new Dictionary<string, ConversationPatch> { [patch.ConversationName] = patch });

    [Fact]
    public void Detect_BothPacksAddTheSameNodeId_ReportsAnAddedNodeConflict()
    {
        // Two mods built on the same base conversation allocate new ids from the same
        // max+1, so this is the most likely real-world clash: the later pack's node
        // replaces the earlier one wholesale, and the earlier pack's links then point
        // at a node it never wrote.
        var projects = new[]
        {
            Project("ModA", new ConversationPatch("conv1", 2, [AddedNode(500, "from A")], [], [])),
            Project("ModB", new ConversationPatch("conv1", 2, [AddedNode(500, "from B")], [], [])),
        };

        var c = Assert.Single(ConflictDetector.Detect(projects));

        Assert.Equal(PatchConflictKind.AddedNode, c.Kind);
        Assert.Equal(("conv1", 500, 0, 1), (c.ConversationName, c.NodeId, c.FirstPatchIndex, c.SecondPatchIndex));
        Assert.False(c.IsDeletion);
    }

    [Fact]
    public void Detect_BothPacksModifyTheSameLink_ReportsALinkConflict()
    {
        var projects = new[]
        {
            Project("ModA", new ConversationPatch("conv1", 2, [], [], [LinkEdit(5, 9, weight: 2)])),
            Project("ModB", new ConversationPatch("conv1", 2, [], [], [LinkEdit(5, 9, weight: 3)])),
        };

        var c = Assert.Single(ConflictDetector.Detect(projects));

        Assert.Equal(PatchConflictKind.Link, c.Kind);
        Assert.Equal(5, c.NodeId);
        Assert.Equal(9, c.LinkToNodeId);
        Assert.False(c.IsDeletion);
    }

    [Fact]
    public void Detect_OnePackAddsALinkAnotherModifies_ReportsALinkConflict()
    {
        // PatchMerger keys added and modified links by target alike, so an add and a modify
        // of the same from→to link contest the same slot.
        var projects = new[]
        {
            Project("ModA", new ConversationPatch("conv1", 2, [], [], [LinkEdit(5, 9, added: true)])),
            Project("ModB", new ConversationPatch("conv1", 2, [], [], [LinkEdit(5, 9)])),
        };

        var c = Assert.Single(ConflictDetector.Detect(projects));
        Assert.Equal((PatchConflictKind.Link, 9), (c.Kind, c.LinkToNodeId));
    }

    [Fact]
    public void Detect_DifferentLinksOnTheSameNode_AreNotAConflict()
    {
        var projects = new[]
        {
            Project("ModA", new ConversationPatch("conv1", 2, [], [], [LinkEdit(5, 9)])),
            Project("ModB", new ConversationPatch("conv1", 2, [], [], [LinkEdit(5, 10)])),
        };

        Assert.Empty(ConflictDetector.Detect(projects));
    }

    [Fact]
    public void Detect_ExistingKinds_AreReportedAsFieldAndDeletion()
    {
        var projects = new[]
        {
            Project("ModA", MakePatch("conv1", 5, "DefaultText", "Hello", "Hi")),
            Project("ModB", MakePatch("conv1", 5, "DefaultText", "Hello", "Hey")),
            Project("ModC", new ConversationPatch("conv1", 2, [], [5], [])),
        };

        var kinds = ConflictDetector.Detect(projects).Select(c => c.Kind).Distinct().Order().ToList();
        Assert.Equal([PatchConflictKind.Field, PatchConflictKind.Deletion], kinds);
    }

    // ── Issue #100: text edits live in Translations, so the detector must look there ──

    private static ConversationPatch Text(string lang, int nodeId, string text,
                                          IReadOnlyList<NodeEditSnapshot>? added = null) =>
        new("conv1", 2, added ?? [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                { [lang] = [new NodeTranslation(nodeId, text, "")] },
        };

    [Fact]
    public void Detect_BothPacksRewriteTheSameLineInTheSameLanguage_ReportsATextConflict()
    {
        var projects = new[]
        {
            Project("ModA", Text("en", 5, "Hi")),
            Project("ModB", Text("en", 5, "Hey")),
        };

        var c = Assert.Single(ConflictDetector.Detect(projects));

        Assert.Equal(PatchConflictKind.Text, c.Kind);
        Assert.Equal("en", c.Language);
        Assert.Equal(("conv1", 5, 0, 1), (c.ConversationName, c.NodeId, c.FirstPatchIndex, c.SecondPatchIndex));
        Assert.False(c.IsDeletion);
    }

    [Fact]
    public void Detect_SameLineInDifferentLanguages_IsNotAConflict()
    {
        // A French translation pack over an English content mod is complementary.
        var projects = new[]
        {
            Project("ModA", Text("en", 5, "Hi")),
            Project("ModB", Text("fr", 5, "Salut")),
        };

        Assert.Empty(ConflictDetector.Detect(projects));
    }

    [Fact]
    public void Detect_BothPacksAddTheSameNode_ReportsOnlyTheAddedNodeConflict()
    {
        // The added-node conflict already says the later node replaces the earlier one
        // wholesale, text included; a second row for its text would be noise.
        var projects = new[]
        {
            Project("ModA", Text("en", 500, "from A", [AddedNode(500, "")])),
            Project("ModB", Text("en", 500, "from B", [AddedNode(500, "")])),
        };

        Assert.Equal(PatchConflictKind.AddedNode, Assert.Single(ConflictDetector.Detect(projects)).Kind);
    }

    [Fact]
    public void Detect_OnePackDeletesALineAnotherRewrites_ReportsADeletionConflict()
    {
        var projects = new[]
        {
            Project("ModA", Text("en", 5, "Hi")),
            Project("ModB", new ConversationPatch("conv1", 2, [], [5], [])),
        };

        var c = Assert.Single(ConflictDetector.Detect(projects));
        Assert.True(c.IsDeletion);
        Assert.Equal((1, 0), (c.FirstPatchIndex, c.SecondPatchIndex));
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

    // ── Issue #100: translations and translator comments survive the merge ──────
    // Since schema 2 node text lives only in Translations (DiffEngine no longer emits
    // DefaultText/FemaleText field changes), so a merge that drops Translations loses
    // every line of text from every mod that shares a conversation with another.

    private static ConversationPatch TextPatch(
        params (string Lang, int NodeId, string Text)[] entries) =>
        new("conv", ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = entries
                .GroupBy(e => e.Lang)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<NodeTranslation>)g
                        .Select(e => new NodeTranslation(e.NodeId, e.Text, ""))
                        .ToList()),
        };

    private static string? TextOf(ConversationPatch patch, string lang, int nodeId) =>
        patch.Translations.TryGetValue(lang, out var list)
            ? list.SingleOrDefault(t => t.NodeId == nodeId)?.DefaultText
            : null;

    [Fact]
    public void Merge_KeepsTranslationsFromEveryPatch_LaterWinsPerLanguageAndNode()
    {
        var patch1 = TextPatch(("en", 1, "A1"), ("en", 2, "A2"), ("fr", 1, "A1-fr"));
        var patch2 = TextPatch(("en", 2, "B2"), ("en", 3, "B3"), ("de", 3, "B3-de"));

        var merged = PatchMerger.Merge("conv", [patch1, patch2]);

        Assert.Equal("A1",    TextOf(merged, "en", 1));   // only in the earlier patch
        Assert.Equal("B2",    TextOf(merged, "en", 2));   // overlap: later wins
        Assert.Equal("B3",    TextOf(merged, "en", 3));   // only in the later patch
        Assert.Equal("A1-fr", TextOf(merged, "fr", 1));   // language only the earlier has
        Assert.Equal("B3-de", TextOf(merged, "de", 3));   // language only the later has
        Assert.Equal(3, merged.Translations["en"].Count);
    }

    [Fact]
    public void Merge_DropsTranslationsOfDeletedNodes()
    {
        var patch1 = TextPatch(("en", 1, "A1"), ("en", 2, "A2"));
        var patch2 = new ConversationPatch("conv", ConversationPatch.CurrentSchemaVersion, [], [2], []);

        var merged = PatchMerger.Merge("conv", [patch1, patch2]);

        Assert.Equal("A1", TextOf(merged, "en", 1));
        Assert.Null(TextOf(merged, "en", 2));
    }

    [Fact]
    public void Merge_KeepsNodeComments_LaterWinsPerNode()
    {
        var patch1 = new ConversationPatch("conv", 2, [], [], [])
            { NodeComments = new Dictionary<int, string> { [1] = "A1", [2] = "A2" } };
        var patch2 = new ConversationPatch("conv", 2, [], [], [])
            { NodeComments = new Dictionary<int, string> { [2] = "B2" } };

        var merged = PatchMerger.Merge("conv", [patch1, patch2]);

        Assert.Equal(new Dictionary<int, string> { [1] = "A1", [2] = "B2" }, merged.NodeComments);
    }
}
