using System.Xml.Linq;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;
using DialogEditor.Core.Serialization;

namespace DialogEditor.Tests.Serialization;

public class Poe1ConversationSerializerTests
{
    private const string TwoNodeXml = """
        <FlowChartFile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <Nodes>
            <FlowChartNode xsi:type="TalkNode">
              <NodeID>0</NodeID>
              <SpeakerGuid>aaaa</SpeakerGuid><ListenerGuid>bbbb</ListenerGuid>
              <Links>
                <FlowChartLink>
                  <FromNodeID>0</FromNodeID><ToNodeID>1</ToNodeID>
                  <RandomWeight>1</RandomWeight>
                  <QuestionNodeTextDisplay>ShowOnce</QuestionNodeTextDisplay>
                  <Conditionals><Components/></Conditionals>
                </FlowChartLink>
              </Links>
              <Conditionals><Components>
                <ExpressionComponent><Data><FullName>SomeCond</FullName><Parameters/></Data></ExpressionComponent>
              </Components></Conditionals>
              <OnEnterScripts/><OnExitScripts/><OnUpdateScripts/>
              <DisplayType>Conversation</DisplayType><Persistence>None</Persistence>
              <ActorDirection/><Comments/><VOFilename/>
            </FlowChartNode>
            <FlowChartNode xsi:type="TalkNode">
              <NodeID>1</NodeID>
              <SpeakerGuid>cccc</SpeakerGuid><ListenerGuid>dddd</ListenerGuid>
              <Links/>
              <Conditionals><Components/></Conditionals>
              <OnEnterScripts/><OnExitScripts/><OnUpdateScripts/>
              <DisplayType>Conversation</DisplayType><Persistence>None</Persistence>
              <ActorDirection/><Comments/><VOFilename/>
            </FlowChartNode>
          </Nodes>
        </FlowChartFile>
        """;

    private static NodeEditSnapshot Node(int id, string speaker = "aaaa",
        IReadOnlyList<LinkEditSnapshot>? links = null) =>
        new(id, false, SpeakerCategory.Npc, speaker, "bbbb",
            "text", "", "Conversation", "None", "", "", "", false, false,
            links ?? [], [], []);

    [Fact]
    public void Serialize_UpdatesSpeakerGuid()
    {
        var snapshot = new ConversationEditSnapshot([Node(0, "new-guid"), Node(1)]);
        var result   = Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot);
        var nodes    = Poe1ConversationParser.ParseXml(result);
        Assert.Equal("new-guid", nodes[0].SpeakerGuid);
    }

    [Fact]
    public void Serialize_RoundTripsConditions()
    {
        // Parse the XML to get structured conditions, include them in snapshot,
        // serialize back, parse again — conditions must survive the round-trip.
        var parsedNodes = Poe1ConversationParser.ParseXml(TwoNodeXml);
        var snap0 = Node(0) with { Conditions = parsedNodes[0].Conditions };
        var snapshot = new ConversationEditSnapshot([snap0, Node(1)]);
        var result   = Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot);
        var doc      = XDocument.Parse(result);
        var conds    = doc.Descendants("FlowChartNode").First()
                          .Element("Conditionals")!.Element("Components")!
                          .Elements("ExpressionComponent");
        Assert.NotEmpty(conds);
    }

    [Fact]
    public void Serialize_AddsNewNode_KeepsItsOutgoingLinks()
    {
        // Regression (B-005): a node added by the editor (absent from the original
        // XML) must be written with its outgoing links, or it is a dead end in-game.
        var links    = new[] { new LinkEditSnapshot(99, 1, 1f, "ShowOnce", false) };
        var snapshot = new ConversationEditSnapshot([Node(0), Node(1), Node(99, links: links)]);

        var result = Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot);

        var nodes = Poe1ConversationParser.ParseXml(result);
        var added = nodes.Single(n => n.NodeId == 99);
        var link  = Assert.Single(added.Links);
        Assert.Equal(1, link.ToNodeId);
    }

    [Fact]
    public void Serialize_DeletesRemovedNode()
    {
        var snapshot = new ConversationEditSnapshot([Node(0)]);
        var result   = Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot);
        var nodes    = Poe1ConversationParser.ParseXml(result);
        Assert.DoesNotContain(nodes, n => n.NodeId == 1);
    }

    [Fact]
    public void Serialize_AddsNewNode()
    {
        var snapshot = new ConversationEditSnapshot([Node(0), Node(1), Node(99)]);
        var result   = Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot);
        var nodes    = Poe1ConversationParser.ParseXml(result);
        Assert.Contains(nodes, n => n.NodeId == 99);
    }

    private static NodeEditSnapshot ScriptNode(int id) =>
        new(id, false, SpeakerCategory.Script, "aaaa", "bbbb",
            "text", "", "Conversation", "None", "", "", "", false, false,
            [], [], []);

    private static NodeEditSnapshot NarratorNode(int id) =>
        new(id, false, SpeakerCategory.Narrator, "00000000-0000-0000-0000-000000000000", "bbbb",
            "text", "", "Conversation", "None", "", "", "", false, false,
            [], [], []);

    [Fact]
    public void Serialize_ScriptNode_EmitsScriptNodeXsiType()
    {
        var snapshot = new ConversationEditSnapshot([ScriptNode(0), Node(1)]);
        var result   = Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot);
        var doc      = System.Xml.Linq.XDocument.Parse(result);
        var ns       = System.Xml.Linq.XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance");
        var node0    = doc.Descendants("FlowChartNode").First(n => (int)n.Element("NodeID")! == 0);
        Assert.Equal("ScriptNode", node0.Attribute(ns + "type")?.Value);
    }

    [Fact]
    public void Serialize_NarratorNode_EmitsTalkNodeXsiType()
    {
        var snapshot = new ConversationEditSnapshot([NarratorNode(0), Node(1)]);
        var result   = Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot);
        var doc      = System.Xml.Linq.XDocument.Parse(result);
        var ns       = System.Xml.Linq.XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance");
        var node0    = doc.Descendants("FlowChartNode").First(n => (int)n.Element("NodeID")! == 0);
        Assert.Equal("TalkNode", node0.Attribute(ns + "type")?.Value);
    }

    [Fact]
    public void Serialize_NewScriptNode_EmitsScriptNodeXsiType()
    {
        var snapshot = new ConversationEditSnapshot([Node(0), Node(1), ScriptNode(99)]);
        var result   = Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot);
        var doc      = System.Xml.Linq.XDocument.Parse(result);
        var ns       = System.Xml.Linq.XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance");
        var node99   = doc.Descendants("FlowChartNode").First(n => (int)n.Element("NodeID")! == 99);
        Assert.Equal("ScriptNode", node99.Attribute(ns + "type")?.Value);
    }

    // ── #111: saved files must deserialize with the game's own model ─────────────
    // In OEIFormats, ScriptCall is a plain class; ConditionalCall derives from
    // ExpressionComponent. XmlSerializer rejects <ScriptCall xsi:type="ConditionalCall">
    // ("The specified type was not recognized"), so the game cannot load the file.

    private static readonly XNamespace XsiNs = "http://www.w3.org/2001/XMLSchema-instance";

    private static NodeEditSnapshot WithScript(NodeEditSnapshot n, ScriptCategory category) =>
        n with { Scripts = [new ScriptCall("Void SetGlobal(String, Int32)", ["g_x", "1"], category)] };

    [Theory]
    [InlineData(ScriptCategory.Enter)]
    [InlineData(ScriptCategory.Exit)]
    [InlineData(ScriptCategory.Update)]
    public void Serialize_ExistingNode_ScriptCallsCarryNoXsiType(ScriptCategory category)
    {
        var snapshot = new ConversationEditSnapshot([WithScript(Node(0), category), Node(1)]);
        var doc      = XDocument.Parse(Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot));

        var calls = doc.Descendants("ScriptCall").ToList();
        Assert.Single(calls);
        Assert.Null(calls[0].Attribute(XsiNs + "type"));
    }

    [Fact]
    public void Serialize_NewNode_ScriptCallsCarryNoXsiType()
    {
        var snapshot = new ConversationEditSnapshot(
            [Node(0), Node(1), WithScript(Node(99), ScriptCategory.Enter)]);
        var doc = XDocument.Parse(Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot));

        var calls = doc.Descendants("ScriptCall").ToList();
        Assert.Single(calls);
        Assert.Null(calls[0].Attribute(XsiNs + "type"));
    }

    [Fact]
    public void Serialize_ScriptCalls_StillRoundTrip()
    {
        var snapshot = new ConversationEditSnapshot([WithScript(Node(0), ScriptCategory.Exit), Node(1)]);
        var nodes    = Poe1ConversationParser.ParseXml(
            Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot));

        var script = Assert.Single(nodes.First(n => n.NodeId == 0).Scripts);
        Assert.Equal("Void SetGlobal(String, Int32)", script.FullName);
        Assert.Equal(["g_x", "1"], script.Parameters);
        Assert.Equal(ScriptCategory.Exit, script.Category);
    }

    // A node without <DisplayType>/<Persistence> (e.g. a BankNode) parses to "".
    // Writing that back as an empty element is not a valid enum value for the game's
    // XmlSerializer; leaving it out lets the DialogueNode constructor default apply.

    private const string NodeWithoutEnumsXml = """
        <FlowChartFile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <Nodes>
            <FlowChartNode xsi:type="BankNode">
              <NodeID>0</NodeID>
              <Links/>
              <Conditionals><Components/></Conditionals>
              <OnEnterScripts/><OnExitScripts/><OnUpdateScripts/>
            </FlowChartNode>
          </Nodes>
        </FlowChartFile>
        """;

    private static NodeEditSnapshot NodeWithoutEnums(int id) =>
        Node(id) with { DisplayType = "", Persistence = "" };

    [Theory]
    [InlineData("DisplayType")]
    [InlineData("Persistence")]
    public void Serialize_ExistingNode_DoesNotAddEmptyEnumElement(string element)
    {
        var snapshot = new ConversationEditSnapshot([NodeWithoutEnums(0)]);
        var doc      = XDocument.Parse(Poe1ConversationSerializer.Serialize(NodeWithoutEnumsXml, snapshot));

        Assert.Empty(doc.Descendants(element));
    }

    [Theory]
    [InlineData("DisplayType")]
    [InlineData("Persistence")]
    public void Serialize_NewNode_DoesNotAddEmptyEnumElement(string element)
    {
        var snapshot = new ConversationEditSnapshot([Node(0), Node(1), NodeWithoutEnums(99)]);
        var doc      = XDocument.Parse(Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot));

        var node99 = doc.Descendants("FlowChartNode").First(n => (int)n.Element("NodeID")! == 99);
        Assert.Null(node99.Element(element));
    }

    [Theory]
    [InlineData("DisplayType")]
    [InlineData("Persistence")]
    public void Serialize_ExistingNode_RemovesEmptyEnumElementAlreadyPresent(string element)
    {
        // A file an earlier build already broke: <DisplayType/> must not survive a re-save.
        var broken   = TwoNodeXml.Replace("<DisplayType>Conversation</DisplayType><Persistence>None</Persistence>",
                                          "<DisplayType/><Persistence/>");
        var snapshot = new ConversationEditSnapshot([NodeWithoutEnums(0), NodeWithoutEnums(1)]);
        var doc      = XDocument.Parse(Poe1ConversationSerializer.Serialize(broken, snapshot));

        Assert.Empty(doc.Descendants(element));
    }

    // ── #112: links may omit RandomWeight / QuestionNodeTextDisplay ──────────────
    // 13 shipped conversations (e.g. 03_cv_aldwyn) write links with neither element;
    // the game's DialogueLink constructor defaults them to 1 / ShowOnce. The save must
    // not crash, and must add an element only when the value differs from that default.

    private const string LinkWithoutDefaultsXml = """
        <ConversationData xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <Nodes>
            <FlowChartNode xsi:type="TalkNode">
              <NodeID>0</NodeID>
              <SpeakerGuid>aaaa</SpeakerGuid><ListenerGuid>bbbb</ListenerGuid>
              <Links>
                <FlowChartLink xsi:type="DialogueLink">
                  <FromNodeID>0</FromNodeID><ToNodeID>1</ToNodeID><PointsToGhost>false</PointsToGhost>
                  <Conditionals><Components/></Conditionals><ClassExtender><ExtendedProperties/></ClassExtender>
                </FlowChartLink>
              </Links>
              <Conditionals><Components/></Conditionals>
              <OnEnterScripts/><OnExitScripts/><OnUpdateScripts/>
              <DisplayType>Conversation</DisplayType><Persistence>None</Persistence>
            </FlowChartNode>
            <FlowChartNode xsi:type="TalkNode">
              <NodeID>1</NodeID>
              <SpeakerGuid>cccc</SpeakerGuid><ListenerGuid>dddd</ListenerGuid>
              <Links/>
              <Conditionals><Components/></Conditionals>
              <OnEnterScripts/><OnExitScripts/><OnUpdateScripts/>
              <DisplayType>Conversation</DisplayType><Persistence>None</Persistence>
            </FlowChartNode>
          </Nodes>
        </ConversationData>
        """;

    private static XElement OnlyLink(string xml) => XDocument.Parse(xml).Descendants("FlowChartLink").Single();

    private static string SaveLink(string originalXml, float weight, string display) =>
        Poe1ConversationSerializer.Serialize(originalXml, new ConversationEditSnapshot(
            [Node(0, links: [new LinkEditSnapshot(0, 1, weight, display, false)]), Node(1)]));

    [Fact]
    public void Serialize_LinkWithoutDefaults_Unchanged_DoesNotAddElements()
    {
        // What the parser yields for the missing elements: weight 1, display "".
        var link = OnlyLink(SaveLink(LinkWithoutDefaultsXml, 1f, ""));

        Assert.Null(link.Element("RandomWeight"));
        Assert.Null(link.Element("QuestionNodeTextDisplay"));
        Assert.NotNull(link.Element("PointsToGhost"));   // the rest of the link is preserved
        Assert.NotNull(link.Element("ClassExtender"));
    }

    [Fact]
    public void Serialize_LinkWithoutDefaults_ExplicitDefaults_DoesNotAddElements()
    {
        var link = OnlyLink(SaveLink(LinkWithoutDefaultsXml, 1f, "ShowOnce"));

        Assert.Null(link.Element("RandomWeight"));
        Assert.Null(link.Element("QuestionNodeTextDisplay"));
    }

    [Fact]
    public void Serialize_LinkWithoutDefaults_NonDefaultValues_AreAdded()
    {
        var link = OnlyLink(SaveLink(LinkWithoutDefaultsXml, 3f, "ShowAlways"));

        Assert.Equal("3",          (string?)link.Element("RandomWeight"));
        Assert.Equal("ShowAlways", (string?)link.Element("QuestionNodeTextDisplay"));
    }

    [Fact]
    public void Serialize_LinkWithElements_UpdatesThem()
    {
        // Elements the original file already writes keep being written, even at the default.
        var link = OnlyLink(SaveLink(TwoNodeXml, 1f, "ShowAlways"));

        Assert.Equal("1",          (string?)link.Element("RandomWeight"));
        Assert.Equal("ShowAlways", (string?)link.Element("QuestionNodeTextDisplay"));
    }

    [Fact]
    public void Serialize_LinkWithElements_EmptyDisplay_RemovesElement()
    {
        // "" is not a valid QuestionNodeDisplayType; absent means ShowOnce to the game.
        var link = OnlyLink(SaveLink(TwoNodeXml, 1f, ""));

        Assert.Null(link.Element("QuestionNodeTextDisplay"));
    }

    [Fact]
    public void Serialize_NewLink_DefaultValues_AreOmitted()
    {
        // Importers and the canvas create links with display "" and weight 1.
        var snapshot = new ConversationEditSnapshot(
            [Node(0), Node(1, links: [new LinkEditSnapshot(1, 0, 1f, "", false)])]);
        var doc  = XDocument.Parse(Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot));
        var link = doc.Descendants("FlowChartLink").Single(l => (int)l.Element("FromNodeID")! == 1);

        Assert.Null(link.Element("RandomWeight"));
        Assert.Null(link.Element("QuestionNodeTextDisplay"));
    }

    [Fact]
    public void Serialize_NewLink_NonDefaultValues_AreWritten()
    {
        var snapshot = new ConversationEditSnapshot(
            [Node(0), Node(1, links: [new LinkEditSnapshot(1, 0, 2f, "ShowNever", false)])]);
        var doc  = XDocument.Parse(Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot));
        var link = doc.Descendants("FlowChartLink").Single(l => (int)l.Element("FromNodeID")! == 1);

        Assert.Equal("2",         (string?)link.Element("RandomWeight"));
        Assert.Equal("ShowNever", (string?)link.Element("QuestionNodeTextDisplay"));
    }

    [Fact]
    public void Serialize_LinkWithoutDefaults_RoundTripsThroughParser()
    {
        var nodes = Poe1ConversationParser.ParseXml(SaveLink(LinkWithoutDefaultsXml, 1f, ""));
        var link  = Assert.Single(nodes.First(n => n.NodeId == 0).Links);

        Assert.Equal(1f, link.RandomWeight);
        Assert.Equal("", link.QuestionNodeTextDisplay);
    }

    [Fact]
    public void Serialize_NonEmptyEnumValues_AreStillWritten()
    {
        var snapshot = new ConversationEditSnapshot(
            [Node(0) with { DisplayType = "Bark", Persistence = "OncePerConversation" }, Node(1)]);
        var doc   = XDocument.Parse(Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot));
        var node0 = doc.Descendants("FlowChartNode").First(n => (int)n.Element("NodeID")! == 0);

        Assert.Equal("Bark",                (string?)node0.Element("DisplayType"));
        Assert.Equal("OncePerConversation", (string?)node0.Element("Persistence"));
    }
}
