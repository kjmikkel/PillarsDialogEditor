namespace DialogEditor.Tests.CanonicalMod;

/// One feature of the canonical mod (issue 138): the edit that produces it through the real
/// view models, and the expectation that proves it reached the game files. Expectations
/// describe the FINAL state, so a later step must never undo an earlier step's feature.
/// KnownIssue names the open GitHub issue a failing expectation exposes ("#160"): the edit
/// still runs, so the mod stays complete, but the expectation is reported as skipped.
public sealed record ScenarioStep(
    string Tag,
    string Description,
    Func<CanonicalModSession, Task> Edit,
    Action<ModView> Expect,
    string? KnownIssue = null);
