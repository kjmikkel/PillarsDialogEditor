using DialogEditor.Core.GameData;
using DialogEditor.Patch;

namespace DialogEditor.Tests.GameData;

public class Poe1GameDataProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly Poe1GameDataProvider _provider;

    public Poe1GameDataProviderTests()
    {
        Directory.CreateDirectory(_root);
        _provider = new Poe1GameDataProvider(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // ── Fixtures ──────────────────────────────────────────────────────────

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
              <OnEnterScripts /><OnExitScripts /><OnUpdateScripts />
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
            <FlowChartNode xsi:type="PlayerResponseNode">
              <NodeID>1</NodeID>
              <Comments />
              <PackageID>1</PackageID>
              <ContainerNodeID>-1</ContainerNodeID>
              <Links />
              <ClassExtender><ExtendedProperties /></ClassExtender>
              <Conditionals><Operator>And</Operator><Components /></Conditionals>
              <OnEnterScripts /><OnExitScripts /><OnUpdateScripts />
              <NotSkippable>false</NotSkippable>
              <IsQuestionNode>true</IsQuestionNode>
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
              <SpeakerGuid>b1a8e901-0000-0000-0000-000000000000</SpeakerGuid>
              <ListenerGuid>fb6a7cbb-80b6-4b9c-8a99-41c8a031f380</ListenerGuid>
            </FlowChartNode>
          </Nodes>
        </ConversationData>
        """;

    // Minimal conversation XML that contains a CharacterMapping element
    private const string ConvWithMappingXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <ConversationData>
          <CharacterMappings>
            <CharacterMapping>
              <Guid>aaaaaaaa-0000-0000-0000-000000000001</Guid>
              <InstanceTag>NPC_TestName</InstanceTag>
            </CharacterMapping>
          </CharacterMappings>
          <Nodes />
        </ConversationData>
        """;

    // characters.stringtable with "TestName" as an entry
    private const string CharactersXml = """
        <StringTableFile>
          <Entries>
            <Entry>
              <ID>1</ID>
              <DefaultText>TestName</DefaultText>
            </Entry>
          </Entries>
        </StringTableFile>
        """;

    // ── Helpers ───────────────────────────────────────────────────────────

    private string ConvDir => Path.Combine(_root, "PillarsOfEternity_Data", "data", "conversations");

    private string WriteConv(string name, string xml)
    {
        Directory.CreateDirectory(ConvDir);
        var path = Path.Combine(ConvDir, name + ".conversation");
        File.WriteAllText(path, xml);
        return path;
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void EnumerateConversations_ReturnsConversationFiles()
    {
        WriteConv("conv1", TwoNodeXml);
        WriteConv("conv2", TwoNodeXml);

        var files = _provider.EnumerateConversations();

        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void EnumerateConversations_IgnoresNonConversationFiles()
    {
        WriteConv("valid", TwoNodeXml);
        Directory.CreateDirectory(ConvDir);
        File.WriteAllText(Path.Combine(ConvDir, "notes.txt"), "ignore me");

        var files = _provider.EnumerateConversations();

        Assert.Single(files);
        Assert.Equal("valid", files[0].Name);
    }

    [Fact]
    public void LoadConversation_ReturnsConversationWithNodes()
    {
        var path = WriteConv("test", TwoNodeXml);
        var file = new ConversationFile("test", "", path, "");

        var conversation = _provider.LoadConversation(file);

        Assert.Equal(2, conversation.Nodes.Count);
    }

    [Fact]
    public void SaveConversation_WritesFileToExpectedPath()
    {
        var path = WriteConv("test", TwoNodeXml);
        var file = new ConversationFile("test", "", path, "");
        var conversation = _provider.LoadConversation(file);
        var snapshot = ConversationSnapshotBuilder.Build(conversation);

        _provider.SaveConversation(file, snapshot);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SaveConversation_RoundTrip_PreservesNodeCount()
    {
        var path = WriteConv("test", TwoNodeXml);
        var file = new ConversationFile("test", "", path, "");
        var original = _provider.LoadConversation(file);
        var snapshot = ConversationSnapshotBuilder.Build(original);

        _provider.SaveConversation(file, snapshot);
        var reloaded = _provider.LoadConversation(file);

        Assert.Equal(original.Nodes.Count, reloaded.Nodes.Count);
    }

    [Fact]
    public void LoadSpeakerNames_WithCharactersFile_ReturnsMappings()
    {
        WriteConv("speaker", ConvWithMappingXml);
        var gameDir = Path.Combine(_root, "PillarsOfEternity_Data", "data", "localized", "en", "text", "game");
        Directory.CreateDirectory(gameDir);
        File.WriteAllText(Path.Combine(gameDir, "characters.stringtable"), CharactersXml);

        var names = _provider.LoadSpeakerNames();

        Assert.Contains("aaaaaaaa-0000-0000-0000-000000000001", names.Keys,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadSpeakerNames_WithoutCharactersFile_ReturnsEmpty()
    {
        var names = _provider.LoadSpeakerNames();
        Assert.Empty(names);
    }

    [Fact]
    public void InitializeConversationFile_CreatesFileOnDisk()
    {
        var file = _provider.BuildNewConversationFile("newconv");

        _provider.InitializeConversationFile(file);

        Assert.True(File.Exists(file.ConversationPath));
        Assert.NotEmpty(File.ReadAllText(file.ConversationPath));
    }

    [Fact]
    public void GetStringTablePath_WithLanguage_ReturnsCorrectPath()
    {
        WriteConv("conv1", TwoNodeXml);
        var file   = _provider.EnumerateConversations().First();
        var enPath = _provider.GetStringTablePath(file);
        var frPath = _provider.GetStringTablePath(file, "fr");
        Assert.Contains(Path.Combine("localized", "fr"), frPath);
        Assert.Contains(Path.Combine("localized", "en"), enPath);
        Assert.Equal(Path.GetFileName(enPath), Path.GetFileName(frPath));
    }

    [Fact]
    public void SaveConversation_DoesNotWriteStringtable()
    {
        var path     = WriteConv("test", TwoNodeXml);
        var file     = new ConversationFile("test", "", path, "");
        var conv     = _provider.LoadConversation(file);
        var snap     = ConversationSnapshotBuilder.Build(conv);
        var stPath   = _provider.GetStringTablePath(file);
        var stBefore = File.Exists(stPath) ? File.ReadAllText(stPath) : null;
        _provider.SaveConversation(file, snap);
        var stAfter  = File.Exists(stPath) ? File.ReadAllText(stPath) : null;
        Assert.Equal(stBefore, stAfter);
    }

    private const string TwoVarXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <GlobalVariablesData>
          <Folders />
          <GlobalVariables>
            <GlobalVariable><Tag>bBanterDisabled</Tag><FolderGuid>00000000-0000-0000-0000-000000000000</FolderGuid><InitialValue>0</InitialValue><Comments /><CreatedBy /></GlobalVariable>
            <GlobalVariable><Tag>npc_met_eder</Tag><FolderGuid>00000000-0000-0000-0000-000000000000</FolderGuid><InitialValue>0</InitialValue><Comments /><CreatedBy /></GlobalVariable>
          </GlobalVariables>
        </GlobalVariablesData>
        """;

    [Fact]
    public void LoadGameDataNames_IncludesGlobalVariableKind()
    {
        var designDir = Path.Combine(_root, "PillarsOfEternity_Data", "data", "design", "global");
        Directory.CreateDirectory(designDir);
        File.WriteAllText(Path.Combine(designDir, "game.globalvariables"), TwoVarXml);

        var names = _provider.LoadGameDataNames();

        Assert.True(names.ContainsKey("GlobalVariable"));
        var vars = names["GlobalVariable"];
        Assert.Equal(2, vars.Count);
        Assert.Contains(vars, e => e.Name == "bBanterDisabled");
        Assert.Contains(vars, e => e.Name == "npc_met_eder");
    }

    [Fact]
    public void LoadGameDataNames_NoGlobalVariablesFile_ReturnsEmptyDict()
    {
        var names = _provider.LoadGameDataNames();
        Assert.Empty(names);
    }
}
