namespace DialogEditor.Tests.CanonicalMod;

/// One feature of the canonical mod (issue 138): the edit that produces it through the real
/// view models, and the expectation that proves it reached the game files. Expectations
/// describe the FINAL state, so a later step must never undo an earlier step's feature.
public sealed record ScenarioStep(
    string Tag,
    string Description,
    Func<CanonicalModSession, Task> Edit,
    Action<ModView> Expect);
