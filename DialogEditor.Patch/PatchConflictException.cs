using DialogEditor.Core.Localisation;

namespace DialogEditor.Patch;

// The message is a developer diagnostic: ConflictResolutionDialog renders the four
// structured properties below into its own localised layout, and only the CLI ever
// prints Message. Localising it would produce a translation nobody reads.
[NotLocalised("Diagnostic message; the UI renders the structured properties instead")]
public sealed class PatchConflictException(
    int nodeId,
    string fieldName,
    string expectedFrom,
    string actualValue)
    : Exception($"Patch conflict on node {nodeId} field '{fieldName}': expected '{expectedFrom}' but found '{actualValue}'.")
{
    public int    NodeId        { get; } = nodeId;
    public string FieldName     { get; } = fieldName;
    public string ExpectedFrom  { get; } = expectedFrom;
    public string ActualValue   { get; } = actualValue;
}
