// Run test collections sequentially, for a stronger reason than DialogEditor.Tests has:
// every class here drives a REAL app on the ONE desktop this machine has. xUnit runs
// collections in parallel by default, so two classes mean two live app instances fighting
// over the foreground window — and synthetic input goes wherever the foreground happens to
// be at that instant.
//
// Not hypothetical. Adding WindowEnumerationGuiTests took ActionToolsGuiTests from 12/12
// to 0/12 while both passed in isolation; that class also parks a modal dialog, which
// swallows input aimed at the other instance. The desktop is a global resource and cannot
// be shared, so parallelism here is not a tuning knob but a correctness bug.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
