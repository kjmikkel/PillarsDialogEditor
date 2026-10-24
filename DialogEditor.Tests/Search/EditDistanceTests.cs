using DialogEditor.Core.Search;

namespace DialogEditor.Tests.Search;

public class EditDistanceTests
{
    [Theory]
    [InlineData("force",   "force",   0)]
    [InlineData("frce",    "force",   1)]   // deletion
    [InlineData("forrce",  "force",   1)]   // insertion
    [InlineData("forse",   "force",   1)]   // substitution
    [InlineData("verison", "version", 1)]   // adjacent transposition counts as one edit
    [InlineData("",        "help",    4)]
    [InlineData("Force",   "force",   1)]   // case-sensitive: callers fold case themselves
    public void DamerauLevenshtein_CountsSingleCharacterEdits(string a, string b, int expected)
        => Assert.Equal(expected, EditDistance.DamerauLevenshtein(a, b));
}
