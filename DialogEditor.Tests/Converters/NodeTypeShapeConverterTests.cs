using System.Globalization;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using DialogEditor.Avalonia.Converters;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Converters;

public class NodeTypeShapeConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static Geometry Convert(SpeakerCategory category, string? displayType) =>
        (Geometry)new NodeTypeShapeConverter()
            .Convert([category, displayType], typeof(Geometry), null, Inv)!;

    [AvaloniaTheory]
    [InlineData(SpeakerCategory.Npc)]
    [InlineData(SpeakerCategory.Player)]
    [InlineData(SpeakerCategory.Narrator)]
    [InlineData(SpeakerCategory.Script)]
    public void Convert_NonBark_ReturnsAGeometry(SpeakerCategory category)
        => Assert.NotNull(Convert(category, "Conversation"));

    [AvaloniaFact]
    public void Convert_EachSpeakerCategoryAndBark_ReturnsFiveDistinctShapes()
    {
        var npc      = Convert(SpeakerCategory.Npc,      "Conversation");
        var player   = Convert(SpeakerCategory.Player,   "Conversation");
        var narrator = Convert(SpeakerCategory.Narrator, "Conversation");
        var script   = Convert(SpeakerCategory.Script,   "Conversation");
        var bark     = Convert(SpeakerCategory.Npc,      "Bark");

        var all = new[] { npc, player, narrator, script, bark };
        Assert.Equal(5, all.Distinct().Count());
    }

    [AvaloniaFact]
    public void Convert_BarkOverridesSpeakerCategory()
    {
        var npcBark    = Convert(SpeakerCategory.Npc,    "Bark");
        var playerBark = Convert(SpeakerCategory.Player, "Bark");
        Assert.Same(npcBark, playerBark);
    }

    [AvaloniaFact]
    public void Convert_MissingValues_DefaultsToNpcCircle()
    {
        var defaulted = (Geometry)new NodeTypeShapeConverter()
            .Convert([], typeof(Geometry), null, Inv)!;
        var npc = Convert(SpeakerCategory.Npc, "Conversation");
        Assert.Same(npc, defaulted);
    }
}
