using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Parsing;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;

namespace DialogEditor.Tests.Analytics;

/// Issue 167 was found on the canonical PoE2 conversation (#138): node 4 is a ScriptNode
/// that leads on to node 7 and carries an exit and an update script, neither of which the
/// game runs. This runs the real parser, view model and analysis on the real file. (Its bank
/// and trigger nodes carry no exit or update scripts, so FlowAnalysisServiceTests covers those.)
[Trait("Category", "Canonical")]
public class CanonicalScriptNodeWarningTests
{
    [Fact]
    public void CanonicalPoe2Conversation_FlagsOnlyNode4sScripts()
    {
        var path = Path.Combine(CanonicalFixture.SourceDir("poe2"), "PillarsOfEternityII_Data", "exported",
                                "design", "conversations", "canonical.conversationbundle");
        var snapshot = new ConversationEditSnapshot(Poe2ConversationParser.ParseFile(path)
            .Select(n => new NodeViewModel(n, null).ToSnapshot(
                n.Links.Select(l => new LinkEditSnapshot(l.FromNodeId, l.ToNodeId, l.RandomWeight,
                                                         l.QuestionNodeTextDisplay, l.HasConditions)).ToList()))
            .ToList());

        var flagged = FlowAnalysisService.Analyze(snapshot, "poe2").Issues
            .Where(i => i.Kind is FlowIssueKind.ScriptExitNeverRuns or FlowIssueKind.ScriptUpdateNeverRuns)
            .Select(i => (i.NodeId, i.Kind))
            .Order();

        Assert.Equal([(4, FlowIssueKind.ScriptExitNeverRuns), (4, FlowIssueKind.ScriptUpdateNeverRuns)], flagged);
    }
}
