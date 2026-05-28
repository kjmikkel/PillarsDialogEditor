using DialogEditor.Core.Import;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Import;

public class ArticyXmlImporterTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string WriteTempXml(string content, string? fileName = null)
    {
        var path = fileName is not null
            ? Path.Combine(Path.GetTempPath(), fileName)
            : Path.ChangeExtension(Path.GetTempFileName(), ".xml");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch (Exception) { /* best-effort cleanup */ }
    }

    private static readonly ArticyXmlImporter Importer = new();

    private const string TwoFragmentXml = """
        <ArticyExport>
          <Content>
            <Packages>
              <Package Name="Default">
                <Models>
                  <Dialogue Id="0x00010001" TechnicalName="my_conv" DisplayName="My Conversation"/>
                  <DialogueFragment Id="0x00020001" DisplayName="">
                    <Properties>
                      <Text>Hello adventurer!</Text>
                      <Speaker Id="0x00030001"/>
                    </Properties>
                    <Connections>
                      <OutgoingConnections>
                        <Connection Id="0x00040001" Target="0x00020002"/>
                      </OutgoingConnections>
                    </Connections>
                  </DialogueFragment>
                  <DialogueFragment Id="0x00020002" DisplayName="">
                    <Properties>
                      <Text>Goodbye!</Text>
                    </Properties>
                    <Connections/>
                  </DialogueFragment>
                  <Entity Id="0x00030001" TechnicalName="Player" DisplayName="Player Character"/>
                  <Entity Id="0x00030002" TechnicalName="Narrator" DisplayName="Narrator"/>
                </Models>
              </Package>
            </Packages>
          </Content>
        </ArticyExport>
        """;

    // ── Node count ────────────────────────────────────────────────────────

    [Fact]
    public void Import_BasicFragments_ReturnsCorrectNodeCount()
    {
        var path = WriteTempXml(TwoFragmentXml);

        var result = Importer.Import(path);

        Assert.Equal(2, result.Nodes.Count);
    }

    // ── Text mapping ──────────────────────────────────────────────────────

    [Fact]
    public void Import_TextMappedFromTextElement()
    {
        var path = WriteTempXml(TwoFragmentXml);

        var result = Importer.Import(path);

        Assert.Contains(result.Nodes, n => n.DefaultText == "Hello adventurer!");
        Assert.Contains(result.Nodes, n => n.DefaultText == "Goodbye!");
    }

    // ── Speaker categories ────────────────────────────────────────────────

    [Fact]
    public void Import_SpeakerPlayer_SetsPlayerCategory()
    {
        var path = WriteTempXml(TwoFragmentXml);

        var result = Importer.Import(path);

        var playerNode = result.Nodes.Single(n => n.DefaultText == "Hello adventurer!");
        Assert.Equal(SpeakerCategory.Player, playerNode.SpeakerCategory);
        Assert.True(playerNode.IsPlayerChoice);
    }

    [Fact]
    public void Import_SpeakerNarrator_SetsNarratorCategory()
    {
        const string xml = """
            <ArticyExport>
              <Content>
                <Packages>
                  <Package Name="Default">
                    <Models>
                      <Dialogue Id="0x00010001" TechnicalName="narr_conv" DisplayName="Narr"/>
                      <DialogueFragment Id="0x00020001" DisplayName="">
                        <Properties>
                          <Text>Once upon a time...</Text>
                          <Speaker Id="0x00030002"/>
                        </Properties>
                        <Connections/>
                      </DialogueFragment>
                      <Entity Id="0x00030002" TechnicalName="Narrator" DisplayName="Narrator"/>
                    </Models>
                  </Package>
                </Packages>
              </Content>
            </ArticyExport>
            """;
        var path = WriteTempXml(xml);

        var result = Importer.Import(path);

        var node = result.Nodes.Single();
        Assert.Equal(SpeakerCategory.Narrator, node.SpeakerCategory);
        Assert.False(node.IsPlayerChoice);
    }

    [Fact]
    public void Import_NoSpeaker_DefaultsToNpc()
    {
        const string xml = """
            <ArticyExport>
              <Content>
                <Packages>
                  <Package Name="Default">
                    <Models>
                      <Dialogue Id="0x00010001" TechnicalName="npc_conv" DisplayName="NPC"/>
                      <DialogueFragment Id="0x00020001" DisplayName="">
                        <Properties>
                          <Text>Greetings.</Text>
                        </Properties>
                        <Connections/>
                      </DialogueFragment>
                    </Models>
                  </Package>
                </Packages>
              </Content>
            </ArticyExport>
            """;
        var path = WriteTempXml(xml);

        var result = Importer.Import(path);

        var node = result.Nodes.Single();
        Assert.Equal(SpeakerCategory.Npc, node.SpeakerCategory);
        Assert.False(node.IsPlayerChoice);
    }

    // ── Links ─────────────────────────────────────────────────────────────

    [Fact]
    public void Import_OutgoingConnections_ProducesLinks()
    {
        var path = WriteTempXml(TwoFragmentXml);

        var result = Importer.Import(path);

        // Fragment 0x00020001 maps to int id 1 (lex sort), 0x00020002 to id 2.
        var node1 = result.Nodes.Single(n => n.NodeId == 1);
        var link = node1.Links.Single();
        Assert.Equal(1, link.FromNodeId);
        Assert.Equal(2, link.ToNodeId);
        Assert.Equal(1f, link.RandomWeight);
        Assert.Equal("", link.QuestionNodeTextDisplay);
        Assert.False(link.HasConditions);
        Assert.Null(link.Conditions);
    }

    // ── SuggestedName ─────────────────────────────────────────────────────

    [Fact]
    public void Import_SuggestedName_FromTechnicalName()
    {
        var path = WriteTempXml(TwoFragmentXml);

        var result = Importer.Import(path);

        Assert.Equal("my_conv", result.SuggestedName);
    }

    [Fact]
    public void Import_SuggestedName_FallsBackToFilename()
    {
        const string xml = """
            <ArticyExport>
              <Content>
                <Packages>
                  <Package Name="Default">
                    <Models>
                      <DialogueFragment Id="0x00020001" DisplayName="">
                        <Properties>
                          <Text>Hi.</Text>
                        </Properties>
                        <Connections/>
                      </DialogueFragment>
                    </Models>
                  </Package>
                </Packages>
              </Content>
            </ArticyExport>
            """;
        var path = WriteTempXml(xml, "fallback_name.xml");

        var result = Importer.Import(path);

        Assert.Equal("fallback_name", result.SuggestedName);
    }

    // ── Warnings ──────────────────────────────────────────────────────────

    [Fact]
    public void Import_Articy_HasNoWarnings()
    {
        var path = WriteTempXml(TwoFragmentXml);
        var result = Importer.Import(path);
        Assert.Empty(result.Warnings);
    }

    // ── Error handling ────────────────────────────────────────────────────

    [Fact]
    public void Import_NonArticyXml_ThrowsFormatException()
    {
        const string xml = """
            <SomeOtherRoot>
              <Data/>
            </SomeOtherRoot>
            """;
        var path = WriteTempXml(xml);

        Assert.Throws<FormatException>(() => Importer.Import(path));
    }

    // ── Texts match nodes ─────────────────────────────────────────────────

    [Fact]
    public void Import_TextsMatchNodes()
    {
        var path = WriteTempXml(TwoFragmentXml);

        var result = Importer.Import(path);

        Assert.Equal(result.Nodes.Count, result.Texts.Count);
        foreach (var node in result.Nodes)
        {
            var text = result.Texts.SingleOrDefault(t => t.NodeId == node.NodeId);
            Assert.NotNull(text);
            Assert.Equal(node.DefaultText, text.DefaultText);
            Assert.Equal("", text.FemaleText);
        }
    }
}
