using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class SpeakerLineScannerTests
{
    private const string Bao = "9c5f12c9-e93d-4952-9f1a-726c9498f8fb";

    private static ConversationNode Node(int id, string speaker = Bao) =>
        new(id, false, SpeakerCategory.Npc, speaker, "", [], [], [], "Conversation", "None");

    private static Conversation Conv(string name, int id, string def, string fem = "", string speaker = Bao) =>
        new(name, [Node(id, speaker)], new StringTable([new StringEntry(id, def, fem)]));

    private static ConversationPatch EmptyPatch(string name) =>
        new(name, ConversationPatch.CurrentSchemaVersion, [], [], []);

    [Fact] // Both a patched and an unpatched conversation contribute rows in one scan
    public void Scan_CoversPatchedAndUnpatchedConversations()
    {
        var provider = new FakeGameDataProvider("poe2", "en",
            Conv("patched", 1, "line A"), Conv("vanilla", 2, "line B"));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("patched"));

        var rows = SpeakerLineScanner.Scan(project, provider, "en");

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.ConversationName == "patched" && r.NodeId == 1);
        Assert.Contains(rows, r => r.ConversationName == "vanilla" && r.NodeId == 2);
        // The conversation the project never patches is Vanilla.
        Assert.Equal(LineOrigin.Vanilla, rows.Single(r => r.ConversationName == "vanilla").Origin);
    }

    [Fact] // A node in ModifiedNodes is Edited
    public void Scan_ModifiedNode_TaggedEdited()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", 1, "edited line"));
        var mod = new NodeModification(1, new Dictionary<string, FieldChange>(), [], []);
        var patch = new ConversationPatch("c", ConversationPatch.CurrentSchemaVersion, [], [], [mod]);
        var project = DialogProject.Empty("P").WithPatch(patch);

        var rows = SpeakerLineScanner.Scan(project, provider, "en");

        Assert.Equal(LineOrigin.Edited, Assert.Single(rows).Origin);
    }

    [Fact] // A node in AddedNodes is New; its in-memory text is used
    public void Scan_AddedNode_TaggedNew()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", 1, "base"));
        var added = new NodeEditSnapshot(
            10, false, SpeakerCategory.Npc, Bao, "", "brand new line", "",
            "Conversation", "None", "", "", "", false, false, [], [], []);
        var patch = new ConversationPatch("c", ConversationPatch.CurrentSchemaVersion, [added], [], []);
        var project = DialogProject.Empty("P").WithPatch(patch);

        var rows = SpeakerLineScanner.Scan(project, provider, "en");

        var newRow = Assert.Single(rows, r => r.NodeId == 10);
        Assert.Equal(LineOrigin.New, newRow.Origin);
        Assert.Equal("brand new line", newRow.LineText);
    }

    [Fact] // Female text produces a second row; Default-only produces one
    public void Scan_FemaleText_EmitsExtraRow()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", 1, "he says", "she says"));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("c"));

        var rows = SpeakerLineScanner.Scan(project, provider, "en");

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Variant == LineVariant.Default && r.LineText == "he says");
        Assert.Contains(rows, r => r.Variant == LineVariant.Female  && r.LineText == "she says");
    }

    [Fact] // Empty Default text (Script/blank) yields no row
    public void Scan_EmptyText_Skipped()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", 1, ""));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("c"));

        Assert.Empty(SpeakerLineScanner.Scan(project, provider, "en"));
    }

    [Fact] // The open conversation uses the live snapshot, not the on-disk base
    public void Scan_OpenConversation_UsesLiveSnapshot()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", 1, "on disk"));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("c"));
        var live = new ConversationEditSnapshot([new NodeEditSnapshot(
            1, false, SpeakerCategory.Npc, Bao, "", "unsaved edit", "",
            "Conversation", "None", "", "", "", false, false, [], [], [])]);

        var rows = SpeakerLineScanner.Scan(project, provider, "en",
            openConversationName: "c", openSnapshot: live);

        Assert.Equal("unsaved edit", Assert.Single(rows).LineText);
    }

    [Fact] // A blank speaker GUID is not attributable — skipped
    public void Scan_BlankSpeaker_Skipped()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", 1, "narrated", speaker: ""));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("c"));

        Assert.Empty(SpeakerLineScanner.Scan(project, provider, "en"));
    }

    [Fact] // An already-cancelled token aborts the scan
    public void Scan_CancelledToken_Throws()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", 1, "x"));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("c"));

        Assert.Throws<OperationCanceledException>(() =>
            SpeakerLineScanner.Scan(project, provider, "en", ct: new CancellationToken(canceled: true)));
    }
}
