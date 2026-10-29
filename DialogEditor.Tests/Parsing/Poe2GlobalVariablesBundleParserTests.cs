using DialogEditor.Core.Parsing;

namespace DialogEditor.Tests.Parsing;

/// Issue 199: Deadfire declares its global variables in *.globalvariablesbundle (JSON), keyed on
/// Tag, which is the string conditions and scripts store. The IDs are all zero.
public class Poe2GlobalVariablesBundleParserTests
{
    private const string Bundle = """
        {"Hash": -41516326,
         "GlobalVariablesSets": [
           { "GlobalVariables": [
               { "ID": "00000000-0000-0000-0000-000000000000", "Tag": "n_(NPC)_State", "InitialValue": 0 },
               { "ID": "00000000-0000-0000-0000-000000000000", "Tag": "b_Met_Eder", "InitialValue": 1 },
               { "ID": "00000000-0000-0000-0000-000000000000", "Tag": "", "InitialValue": 0 }
           ] }
         ]}
        """;

    [Fact]
    public void Parse_ListsEachTag_WithAnEmptyId()
    {
        var vars = Poe2GlobalVariablesBundleParser.Parse(Bundle);

        Assert.Equal(["n_(NPC)_State", "b_Met_Eder"], vars.Select(v => v.Name));
        Assert.All(vars, v => Assert.Equal(string.Empty, v.Id));
    }

    [Fact]
    public void Parse_ToleratesRawLineBreaksInsideStrings()
    {
        var json = Bundle.Replace("\"InitialValue\": 1 }", "\"InitialValue\": 1, \"Comments\": \"two\nlines\" }");

        Assert.Contains(Poe2GlobalVariablesBundleParser.Parse(json), v => v.Name == "b_Met_Eder");
    }

    [Fact]
    public void ParseFile_MissingFile_ReturnsEmpty()
        => Assert.Empty(Poe2GlobalVariablesBundleParser.ParseFile(
            Path.Combine(Path.GetTempPath(), "does-not-exist.globalvariablesbundle")));

    // Issue 345: InitialValue is a JSON integer (GlobalVariablesData.GlobalVariable.InitialValue
    // is an int); shipped bundles hold values from -1 to 10.
    [Fact]
    public void ParseInitialValues_MapsEachTagToItsInitialValue_SkippingEmptyTags()
    {
        var values = Poe2GlobalVariablesBundleParser.ParseInitialValues(Bundle);

        Assert.Equal(2, values.Count);
        Assert.Equal(0, values["n_(NPC)_State"]);
        Assert.Equal(1, values["b_Met_Eder"]);
    }

    [Fact]
    public void ParseInitialValues_NegativeAndMissingValues()
    {
        const string json = """
            {"GlobalVariablesSets": [ { "GlobalVariables": [
                { "Tag": "n_Unset", "InitialValue": -1 },
                { "Tag": "n_NoValue" }
            ] } ]}
            """;

        var values = Poe2GlobalVariablesBundleParser.ParseInitialValues(json);

        Assert.Equal(-1, values["n_Unset"]);
        Assert.Equal(0, values["n_NoValue"]);
    }

    // GlobalVariables.LoadGlobalVariablesBundle overwrites a tag it has already seen.
    [Fact]
    public void ParseInitialValues_RepeatedTag_KeepsTheLastValue()
    {
        const string json = """
            {"GlobalVariablesSets": [
                { "GlobalVariables": [ { "Tag": "n_Twice", "InitialValue": 2 } ] },
                { "GlobalVariables": [ { "Tag": "n_Twice", "InitialValue": 5 } ] }
            ]}
            """;

        Assert.Equal(5, Poe2GlobalVariablesBundleParser.ParseInitialValues(json)["n_Twice"]);
    }

    [Fact]
    public void ParseInitialValuesFile_MissingFile_ReturnsEmpty()
        => Assert.Empty(Poe2GlobalVariablesBundleParser.ParseInitialValuesFile(
            Path.Combine(Path.GetTempPath(), "does-not-exist.globalvariablesbundle")));
}
