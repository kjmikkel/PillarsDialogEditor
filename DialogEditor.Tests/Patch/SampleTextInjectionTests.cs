using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Patch;

/// <summary>
/// The sample project's prose is what a first-time user actually reads, so it is
/// translatable. DialogEditor.Patch cannot reach Loc (it references only Core), so
/// SampleProjectService takes the text as data and the ViewModels layer supplies it
/// from Strings.axaml — see SampleTextResourceTests for that half.
///
/// These tests use obviously-not-English markers so a service that quietly fell back
/// to its own literals could not pass.
/// </summary>
public class SampleTextInjectionTests
{
    private sealed class OkGit : IGitRunner
    {
        public GitResult Run(string workingDirectory, params string[] args) => new(0, "", "");
    }

    private sealed class RecordingGit : IGitRunner
    {
        public List<string[]> Calls { get; } = [];
        public GitResult Run(string workingDirectory, params string[] args)
        {
            Calls.Add(args);
            return new GitResult(0, "", "");
        }
    }

    private static readonly SampleTexts Marked = new(
        AuthorName:       "AUTHOR",
        EditedLineSuffix: " «EDITED»",
        AltLineSuffix:    " «ALT»",
        NewLineText:      "«NEW LINE»",
        TranslatorNote:   "«NOTE»",
        CommitInitial:    "«C1»",
        CommitReshape:    "«C2»",
        CommitExperiment: "«C3»");

    private static Conversation ThreeNodeEder()
    {
        ConversationNode N(int id, int? linkTo) => new(
            NodeId: id, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "",
            Links: linkTo is int t ? [new NodeLink(id, t, [], 1f, "")] : [],
            Conditions: [], Scripts: [], DisplayType: "ConversationLine", Persistence: "None",
            ActorDirection: "", Comments: "", ExternalVO: "", HasVO: false, HideSpeaker: false);

        var strings = new StringTable(new[]
        {
            new StringEntry(1, "Hello there.", ""),
            new StringEntry(2, "I am Eder.", ""),
            new StringEntry(3, "Farewell.", ""),
        });
        return new Conversation(SampleProjectService.Poe1SampleConversation,
            [N(1, 2), N(2, 3), N(3, null)], strings);
    }

    private static SampleBuild Build() =>
        new SampleProjectService(new OkGit())
            .BuildSample(new FakeGameDataProvider("poe1", "en", ThreeNodeEder()), Marked);

    [Fact]
    public void BuildSample_TranslatorNote_ComesFromTheSuppliedText()
    {
        var patch = Build().Final.Patches[SampleProjectService.Poe1SampleConversation];

        Assert.Equal("«NOTE»", patch.NodeComments[1]);
    }

    [Fact]
    public void BuildSample_AddedNodeText_ComesFromTheSuppliedText()
    {
        var patch = Build().Final.Patches[SampleProjectService.Poe1SampleConversation];

        Assert.Contains(patch.Translations["en"], t => t.NodeId == 4 && t.DefaultText == "«NEW LINE»");
    }

    [Fact]
    public void BuildSample_EditedAnchorLine_AppendsTheSuppliedSuffix()
    {
        var patch = Build().Final.Patches[SampleProjectService.Poe1SampleConversation];

        Assert.Contains(patch.Translations["en"],
            t => t.NodeId == 1 && t.DefaultText == "Hello there. «EDITED»");
    }

    [Fact]
    public void BuildSample_CommitMessages_ComeFromTheSuppliedText()
    {
        Assert.Equal(["«C1»", "«C2»", "«C3»"], Build().Commits.Select(c => c.Message));
    }

    [Fact]
    public void BuildSample_ExperimentCommit_UsesTheAlternateSuffix()
    {
        var experiment = Build().Commits.Single(c => c.OnNewExperimentBranch);
        var patch = experiment.Project.Patches[SampleProjectService.Poe1SampleConversation];

        Assert.Contains(patch.Translations["en"],
            t => t.NodeId == 1 && t.DefaultText == "Hello there. «ALT»");
    }

    [Fact]
    public void SeedHistory_ConfiguresTheSuppliedAuthorName()
    {
        // The name shows up as the commit author in the Diff and blame views, so it is
        // display text; the e-mail is a reserved .invalid address and stays fixed.
        var dir = Path.Combine(Path.GetTempPath(), $"sample_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var git = new RecordingGit();
        try
        {
            new SampleProjectService(git).SeedHistory(dir, Build());

            Assert.Contains(git.Calls, c => c is ["config", "user.name", "AUTHOR"]);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best-effort */ }
        }
    }
}
