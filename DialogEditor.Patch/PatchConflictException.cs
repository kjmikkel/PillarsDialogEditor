namespace DialogEditor.Patch;

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
