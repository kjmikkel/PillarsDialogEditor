using System.Text.Json.Nodes;
using DialogEditor.Core.Parsing;

namespace DialogEditor.Tests.Parsing;

/// Issue 222: before #114 the Deadfire DisplayType value was mapped off by one, so every
/// "Conversation" node (value 1) loaded as a bark and the canvas drew every header in the bark
/// colour. These pin the game's enum values.
public class Poe2EnumMapsTests
{
    [Theory]
    [InlineData(0, "Hidden")]
    [InlineData(1, "Conversation")]
    [InlineData(2, "Bark")]
    [InlineData(3, "Overlay")]
    public void DisplayTypeName_UsesTheGamesEnumValues(int value, string expected) =>
        Assert.Equal(expected, Poe2EnumMaps.DisplayTypeName(JsonValue.Create(value)));

    [Theory]
    [InlineData("Conversation", 1)]
    [InlineData("Bark", 2)]
    public void DisplayTypeValue_RoundTrips(string name, int expected) =>
        Assert.Equal(expected, Poe2EnumMaps.DisplayTypeValue(name));
}
