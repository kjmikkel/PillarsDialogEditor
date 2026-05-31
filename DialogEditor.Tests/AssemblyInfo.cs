using Xunit;

// Run test collections sequentially. Several test classes mutate process-global
// static state (e.g. AppSettings.LastProjectPath, Loc.Configure), and xUnit runs
// collections in parallel by default, which causes cross-class races and flaky
// failures (notably MainWindowViewModelTests.ReopenLastProjectOnStartup_...).
// The full suite runs in ~3s, so serial execution costs little for full reliability.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
