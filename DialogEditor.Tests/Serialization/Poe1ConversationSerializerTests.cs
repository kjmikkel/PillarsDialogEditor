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
            links ?? []);

    [Fact]
    public void Serialize_UpdatesSpeakerGuid()
    {
        var snapshot = new ConversationEditSnapshot([Node(0, "new-guid"), Node(1)]);
        var result   = Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot);
        var nodes    = Poe1ConversationParser.ParseXml(result);
        Assert.Equal("new-guid", nodes[0].SpeakerGuid);
    }

    [Fact]
    public void Serialize_PreservesOriginalConditions()
    {
        var snapshot = new ConversationEditSnapshot([Node(0), Node(1)]);
        var result   = Poe1ConversationSerializer.Serialize(TwoNodeXml, snapshot);
        var doc      = XDocument.Parse(result);
        var conds    = doc.Descendants("FlowChartNode").First()
                          .Element("Conditionals")!.Element("Components")!
                          .Elements("ExpressionComponent");
        Assert.NotEmpty(conds);
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
}
