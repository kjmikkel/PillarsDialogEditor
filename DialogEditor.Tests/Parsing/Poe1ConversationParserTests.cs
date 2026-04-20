using DialogEditor.Core.Parsing;

namespace DialogEditor.Tests.Parsing;

public class Poe1ConversationParserTests
{
    private const string TwoNodeXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <ConversationData xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          xmlns:xsd="http://www.w3.org/2001/XMLSchema">
          <NextNodeID>2</NextNodeID>
          <Nodes>
            <FlowChartNode xsi:type="TalkNode">
              <NodeID>0</NodeID>
              <Comments />
              <PackageID>1</PackageID>
              <ContainerNodeID>-1</ContainerNodeID>
              <Links>
                <FlowChartLink xsi:type="DialogueLink">
                  <FromNodeID>0</FromNodeID>
                  <ToNodeID>1</ToNodeID>
                  <PointsToGhost>false</PointsToGhost>
                  <ClassExtender><ExtendedProperties /></ClassExtender>
                  <RandomWeight>1</RandomWeight>
                  <PlayQuestionNodeVO>true</PlayQuestionNodeVO>
                  <QuestionNodeTextDisplay>ShowOnce</QuestionNodeTextDisplay>
                </FlowChartLink>
              </Links>
              <ClassExtender><ExtendedProperties /></ClassExtender>
              <Conditionals><Operator>And</Operator><Components /></Conditionals>
              <OnEnterScripts />
              <OnExitScripts />
              <OnUpdateScripts />
              <NotSkippable>false</NotSkippable>
              <IsQuestionNode>false</IsQuestionNode>
              <IsTempText>false</IsTempText>
              <PlayVOAs3DSound>false</PlayVOAs3DSound>
              <PlayType>Normal</PlayType>
              <Persistence>None</Persistence>
              <NoPlayRandomWeight>0</NoPlayRandomWeight>
              <DisplayType>Conversation</DisplayType>
              <VOFilename /><VoiceType />
              <ExcludedSpeakerClasses /><ExcludedListenerClasses />
              <IncludedSpeakerClasses /><IncludedListenerClasses />
              <ActorDirection />
              <SpeakerGuid>fb6a7cbb-80b6-4b9c-8a99-41c8a031f380</SpeakerGuid>
              <ListenerGuid>b1a8e901-0000-0000-0000-000000000000</ListenerGuid>
            </FlowChartNode>
            <FlowChartNode xsi:type="TalkNode">
              <NodeID>1</NodeID>
              <Comments />
              <PackageID>1</PackageID>
              <ContainerNodeID>-1</ContainerNodeID>
              <Links />
              <ClassExtender><ExtendedProperties /></ClassExtender>
              <Conditionals>
                <Operator>And</Operator>
                <Components>
                  <ExpressionComponent xsi:type="ConditionalCall">
                    <Data>
                      <FullName>Boolean IsGlobalValue(String, Operator, Int32)</FullName>
                      <Parameters>
                        <string>some_flag</string>
                        <string>EqualTo</string>
                        <string>1</string>
                      </Parameters>
                    </Data>
                    <Not>false</Not>
                    <Operator>And</Operator>
                  </ExpressionComponent>
                </Components>
              </Conditionals>
              <OnEnterScripts />
              <OnExitScripts />
              <OnUpdateScripts />
              <NotSkippable>false</NotSkippable>
              <IsQuestionNode>true</IsQuestionNode>
              <IsTempText>false</IsTempText>
              <PlayVOAs3DSound>false</PlayVOAs3DSound>
              <PlayType>Normal</PlayType>
              <Persistence>OnceEver</Persistence>
              <NoPlayRandomWeight>0</NoPlayRandomWeight>
              <DisplayType>Bark</DisplayType>
              <VOFilename /><VoiceType />
              <ExcludedSpeakerClasses /><ExcludedListenerClasses />
              <IncludedSpeakerClasses /><IncludedListenerClasses />
              <ActorDirection />
              <SpeakerGuid>b1a8e901-0000-0000-0000-000000000000</SpeakerGuid>
              <ListenerGuid>fb6a7cbb-80b6-4b9c-8a99-41c8a031f380</ListenerGuid>
            </FlowChartNode>
          </Nodes>
        </ConversationData>
        """;

    [Fact]
    public void Parse_ReturnsTwoNodes()
    {
        var nodes = Poe1ConversationParser.ParseXml(TwoNodeXml);
        Assert.Equal(2, nodes.Count);
    }

    [Fact]
    public void Parse_Node0_IsNotPlayerChoice()
    {
        var nodes = Poe1ConversationParser.ParseXml(TwoNodeXml);
        Assert.False(nodes[0].IsPlayerChoice);
    }

    [Fact]
    public void Parse_Node1_IsPlayerChoice()
    {
        var nodes = Poe1ConversationParser.ParseXml(TwoNodeXml);
        Assert.True(nodes[1].IsPlayerChoice);
    }

    [Fact]
    public void Parse_Node0_HasOneLink()
    {
        var nodes = Poe1ConversationParser.ParseXml(TwoNodeXml);
        Assert.Single(nodes[0].Links);
        Assert.Equal(1, nodes[0].Links[0].ToNodeId);
    }

    [Fact]
    public void Parse_Node1_HasCondition()
    {
        var nodes = Poe1ConversationParser.ParseXml(TwoNodeXml);
        Assert.True(nodes[1].HasConditions);
        Assert.Single(nodes[1].ConditionStrings);
        Assert.Equal("IsGlobalValue(some_flag, EqualTo, 1)", nodes[1].ConditionStrings[0]);
    }

    [Fact]
    public void Parse_Node1_HasCorrectDisplayTypeAndPersistence()
    {
        var nodes = Poe1ConversationParser.ParseXml(TwoNodeXml);
        Assert.Equal("Bark", nodes[1].DisplayType);
        Assert.Equal("OnceEver", nodes[1].Persistence);
    }

    [Fact]
    public void Parse_Node0_SpeakerGuidCorrect()
    {
        var nodes = Poe1ConversationParser.ParseXml(TwoNodeXml);
        Assert.Equal("fb6a7cbb-80b6-4b9c-8a99-41c8a031f380", nodes[0].SpeakerGuid);
    }
}
