# UIA MCP Server — Foundation Implementation Plan (Phases 1–2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a stdio MCP server that launches the Dialog Editor safely, inspects its UI Automation tree with unambiguous addressing, and runs builds and tests with parsed results.

**Architecture:** Two new projects under `tools/`. `DialogEditor.UiaMcp.Core` is plain `net8.0` with no UI Automation dependency — it holds the settings state machine, output parsers, selector/ref addressing and the resolver, all unit-tested against an in-memory fake tree. `DialogEditor.UiaMcp` is `net8.0-windows` with `UseWPF`, hosting the MCP server and the only code that touches live UIA. The seam between them is `IUiaTree`, which speaks in plain `ElementInfo` records rather than `AutomationElement`, which is exactly what keeps Core testable from the existing `net8.0` test project.

**Tech Stack:** .NET 8, `ModelContextProtocol` 2.2.0 (stdio transport), `Microsoft.Extensions.Hosting` 10.0.11, xunit 2.5.3, UI Automation client (`UIAutomationClient` / `UIAutomationTypes`, supplied by `UseWPF`).

**Spec:** `docs/superpowers/specs/2026-09-03-uia-mcp-server-design.md`

## Scope of this plan

The spec sequences four steps. This plan covers **Step 1 (session + build/test tools)** and **Step 2 (`read_tree`/`find` + resolver)**. It stops before Step 3 (actions with warned fallbacks) and Step 4 (deleting fallbacks as issue #15 phases land), which get their own plan.

That boundary is the spec's own: steps 1–2 carry none of the GUI-*driving* risk and stand alone. On completion the server can launch the app safely, dump and search its tree with refs and addressability warnings, enumerate menus, build the solution, and run tests — useful on its own, and every piece of new logic in it is unit-testable.

## Deviation from the spec, and why

The spec puts tier-2 integration tests behind `[Trait("Category","Gui")]` in a test
project. This plan uses **manual verification via `drive.ps1`** instead, because
`DialogEditor.Tests` targets `net8.0` and cannot reference the `net8.0-windows` server
project — a Gui-trait test needs a second test project. That project is deferred to the
phase that genuinely needs it (actions, where a wrong click has consequences); the
inspection tools in this plan are read-only, so a scripted smoke test is proportionate.

This is a real reduction in automated coverage for tasks 8–10 and should be revisited in
the Phase 3 plan rather than forgotten.

## Global Constraints

- **TDD is mandatory.** A failing test precedes every piece of non-trivial logic. Never write implementation before its red test exists.
- **No bare `catch { }` in production code.** Every caught exception is logged via `AppLog.Error(...)`/`AppLog.Warn(...)`. `OperationCanceledException` is the sole exception and is swallowed silently. Best-effort cleanup in *test* teardown may swallow silently.
- **No in-app component.** Nothing in this plan modifies `DialogEditor.Avalonia`. No remote-control endpoint, network test hook, or automation backdoor. The server drives the app from outside only.
- **stdout is the JSON-RPC channel.** All logging goes to stderr. A single stray `Console.WriteLine` corrupts the protocol.
- **Settings paths are injected, never static.** `DialogEditor.Tests` runs serially because `AppSettings`/`Loc` are global; do not add new global state.
- **Localisation:** no user-visible strings are added by this plan (the server has no UI). The app's own resource rules are untouched.
- **Issue tracking:** GitHub Issues only. Do not add entries to `BUGS.md` or `Gaps.md`, which are retired and read-only.
- Package versions are pinned exactly: `ModelContextProtocol` `2.2.0`, `Microsoft.Extensions.Hosting` `10.0.11`.
- Test project target framework is `net8.0`; `DialogEditor.UiaMcp.Core` must therefore target `net8.0` and must not reference any UIA assembly.

---

### Task 1: Core project scaffolding + `SettingsGuard`

The settings state machine is first because it is the safety property everything else depends on: a real incident during the audit left the user's `LastProjectPath` pointing at a deleted temp project.

**Files:**
- Create: `tools/DialogEditor.UiaMcp.Core/DialogEditor.UiaMcp.Core.csproj`
- Create: `tools/DialogEditor.UiaMcp.Core/SettingsGuard.cs`
- Modify: `DialogEditor.slnx` (add the project)
- Modify: `DialogEditor.Tests/DialogEditor.Tests.csproj` (add ProjectReference)
- Test: `DialogEditor.Tests/UiaMcp/SettingsGuardTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `SettingsGuard(string settingsPath, string backupPath)` with `string? RecoverStaleBackup()`, `void Backup()`, `string Restore()`, `void SetLastProjectPath(string path)`.

- [x] **Step 1: Create the Core project**

`tools/DialogEditor.UiaMcp.Core/DialogEditor.UiaMcp.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Plain net8.0 on purpose: no UI Automation reference lives here, which is
         what lets DialogEditor.Tests (net8.0) reference and unit-test it. -->
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

Add to `DialogEditor.slnx`, after the `DialogEditor.PatchCli` line:

```xml
  <Project Path="tools/DialogEditor.UiaMcp.Core/DialogEditor.UiaMcp.Core.csproj" />
```

Add to `DialogEditor.Tests/DialogEditor.Tests.csproj`, inside the existing `ItemGroup` of `ProjectReference`s:

```xml
    <ProjectReference Include="..\tools\DialogEditor.UiaMcp.Core\DialogEditor.UiaMcp.Core.csproj" />
```

- [x] **Step 2: Write the failing tests**

`DialogEditor.Tests/UiaMcp/SettingsGuardTests.cs`:

```csharp
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

/// <summary>
/// The settings lifecycle ported from tools/ui-automation/DriveApp.ps1 and hardened.
/// The backup lives in a FILE rather than memory because a verification run can span
/// several processes, and because a crashed run must not strand the user's real
/// LastProjectPath — which is exactly what happened before this server existed.
/// </summary>
public class SettingsGuardTests : IDisposable
{
    private readonly string _dir;
    private readonly string _settings;
    private readonly string _backup;

    public SettingsGuardTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "uiamcp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _settings = Path.Combine(_dir, "settings.json");
        _backup = Path.Combine(_dir, "settings.backup.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private SettingsGuard NewGuard() => new(_settings, _backup);

    private void WriteSettings(string lastProject, string theme = "Dark") =>
        File.WriteAllText(_settings,
            $$"""{"LastProjectPath":"{{lastProject}}","Theme":"{{theme}}"}""");

    [Fact]
    public void BackupCopiesSettingsToBackupPath()
    {
        WriteSettings("C:/real/project.dialogproject");
        NewGuard().Backup();
        Assert.True(File.Exists(_backup));
        Assert.Contains("C:/real/project.dialogproject", File.ReadAllText(_backup));
    }

    [Fact]
    public void BackupDoesNotOverwriteAnExistingBackup()
    {
        // A previous run died before restoring: the OLDER backup holds the genuine
        // settings, so a second Backup() must not clobber it with run-mutated state.
        File.WriteAllText(_backup, """{"LastProjectPath":"C:/genuine.dialogproject"}""");
        WriteSettings("C:/temp/scratch.dialogproject");

        NewGuard().Backup();

        Assert.Contains("C:/genuine.dialogproject", File.ReadAllText(_backup));
    }

    [Fact]
    public void RestoreCopiesBackupBackAndRemovesIt()
    {
        WriteSettings("C:/real/project.dialogproject");
        var guard = NewGuard();
        guard.Backup();
        WriteSettings("C:/temp/scratch.dialogproject");

        guard.Restore();

        Assert.Contains("C:/real/project.dialogproject", File.ReadAllText(_settings));
        Assert.False(File.Exists(_backup));
    }

    [Fact]
    public void RestoreWithoutABackupReportsThatNothingWasRestored()
    {
        WriteSettings("C:/temp/scratch.dialogproject");
        var message = NewGuard().Restore();
        Assert.Contains("no backup", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecoverStaleBackupRestoresLeftoverStateAndReportsIt()
    {
        // Server startup after a crashed session.
        File.WriteAllText(_backup, """{"LastProjectPath":"C:/genuine.dialogproject"}""");
        WriteSettings("C:/temp/vanished.dialogproject");

        var message = NewGuard().RecoverStaleBackup();

        Assert.NotNull(message);
        Assert.Contains("C:/genuine.dialogproject", File.ReadAllText(_settings));
        Assert.False(File.Exists(_backup));
    }

    [Fact]
    public void RecoverStaleBackupReturnsNullWhenThereIsNothingToRecover()
    {
        WriteSettings("C:/real/project.dialogproject");
        Assert.Null(NewGuard().RecoverStaleBackup());
    }

    [Fact]
    public void SetLastProjectPathPreservesEveryOtherKey()
    {
        WriteSettings("C:/real/project.dialogproject", theme: "Light");

        NewGuard().SetLastProjectPath("");

        var json = File.ReadAllText(_settings);
        Assert.Contains("\"LastProjectPath\": \"\"", json);
        Assert.Contains("Light", json);
    }
}
```

- [x] **Step 3: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~SettingsGuardTests"`
Expected: FAIL — `SettingsGuard` does not exist (CS0246).

- [x] **Step 4: Write the minimal implementation**

`tools/DialogEditor.UiaMcp.Core/SettingsGuard.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DialogEditor.UiaMcp.Core;

/// <summary>
/// Protects the user's real editor settings across an automation run.
///
/// Ported from DriveApp.ps1's settings lifecycle. Two rules are load-bearing and
/// both come from real incidents:
///   * the snapshot is a FILE, not a field — a run can span processes, and an
///     in-memory-only backup makes Restore a silent no-op in any later one;
///   * an existing backup is NEVER overwritten — if a previous run died before
///     restoring, the older file is the one holding the genuine settings.
/// </summary>
public sealed class SettingsGuard(string settingsPath, string backupPath)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Called at server startup. If a previous run died before restoring, put the
    /// user's settings back and describe what happened; otherwise return null.
    /// </summary>
    public string? RecoverStaleBackup()
    {
        if (!File.Exists(backupPath)) return null;
        var restored = Restore();
        return $"Recovered stale settings backup from a previous run. {restored}";
    }

    public void Backup()
    {
        if (!File.Exists(settingsPath)) return;
        if (File.Exists(backupPath)) return;   // older backup wins — see class remarks
        File.Copy(settingsPath, backupPath, overwrite: false);
    }

    /// <summary>Puts settings back. Returns a human-readable outcome for the tool result.</summary>
    public string Restore()
    {
        if (!File.Exists(backupPath))
            return "Restore skipped: no backup found — settings.json still holds whatever this run wrote.";

        File.Copy(backupPath, settingsPath, overwrite: true);
        File.Delete(backupPath);
        return "Settings restored from backup.";
    }

    public void SetLastProjectPath(string path)
    {
        if (!File.Exists(settingsPath)) return;
        var node = JsonNode.Parse(File.ReadAllText(settingsPath))
                   ?? throw new InvalidOperationException($"Unparseable settings file: {settingsPath}");
        node["LastProjectPath"] = path;
        File.WriteAllText(settingsPath, node.ToJsonString(Indented));
    }
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~SettingsGuardTests"`
Expected: PASS, 7 tests.

- [x] **Step 6: Commit**

```bash
git add tools/DialogEditor.UiaMcp.Core DialogEditor.slnx DialogEditor.Tests/DialogEditor.Tests.csproj DialogEditor.Tests/UiaMcp/SettingsGuardTests.cs
git commit -m "feat(uiamcp): add SettingsGuard with stale-backup recovery"
```

---

### Task 2: `BuildOutputParser`

**Files:**
- Create: `tools/DialogEditor.UiaMcp.Core/BuildOutputParser.cs`
- Test: `DialogEditor.Tests/UiaMcp/BuildOutputParserTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `record Diagnostic(string Severity, string File, int Line, int Column, string Code, string Message)`; `record BuildResult(bool Succeeded, int ErrorCount, int WarningCount, IReadOnlyList<Diagnostic> Diagnostics, bool Truncated)`; `static BuildResult BuildOutputParser.Parse(string output, int maxDiagnostics = 20)`.

- [x] **Step 1: Write the failing tests**

`DialogEditor.Tests/UiaMcp/BuildOutputParserTests.cs`:

```csharp
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

public class BuildOutputParserTests
{
    // Shape taken verbatim from a real `dotnet build DialogEditor.slnx` run.
    private const string RealWarningLine =
        @"C:\repo\DialogEditor.ViewModels\Services\IVoAudioPlayer.cs(30,26): warning CS0067: The event 'NullVoAudioPlayer.PlaybackStopped' is never used [C:\repo\DialogEditor.ViewModels\DialogEditor.ViewModels.csproj]";

    private const string RealErrorLine =
        @"C:\repo\tools\SpikeUiaMcp\Program.cs(83,19): error CS0103: The name 'Path' does not exist in the current context [C:\repo\tools\SpikeUiaMcp\SpikeUiaMcp.csproj]";

    [Fact]
    public void ParsesASuccessfulBuildWithWarnings()
    {
        var output = $"{RealWarningLine}\n    8 Warning(s)\n    0 Error(s)\n\nBuild succeeded.\n";

        var result = BuildOutputParser.Parse(output);

        Assert.True(result.Succeeded);
        Assert.Equal(8, result.WarningCount);
        Assert.Equal(0, result.ErrorCount);
    }

    [Fact]
    public void ParsesAFailedBuild()
    {
        var output = $"{RealErrorLine}\n    0 Warning(s)\n    15 Error(s)\n\nBuild FAILED.\n";

        var result = BuildOutputParser.Parse(output);

        Assert.False(result.Succeeded);
        Assert.Equal(15, result.ErrorCount);
    }

    [Fact]
    public void ExtractsDiagnosticFileLineColumnCodeAndMessage()
    {
        var result = BuildOutputParser.Parse(RealErrorLine + "\nBuild FAILED.\n");

        var d = Assert.Single(result.Diagnostics);
        Assert.Equal("error", d.Severity);
        Assert.EndsWith("Program.cs", d.File);
        Assert.Equal(83, d.Line);
        Assert.Equal(19, d.Column);
        Assert.Equal("CS0103", d.Code);
        Assert.Contains("does not exist", d.Message);
    }

    [Fact]
    public void OrdersErrorsBeforeWarnings()
    {
        var output = $"{RealWarningLine}\n{RealErrorLine}\nBuild FAILED.\n";

        var result = BuildOutputParser.Parse(output);

        Assert.Equal("error", result.Diagnostics[0].Severity);
        Assert.Equal("warning", result.Diagnostics[1].Severity);
    }

    [Fact]
    public void DeduplicatesTheSameDiagnosticReportedOncePerTargetFramework()
    {
        var output = $"{RealErrorLine}\n{RealErrorLine}\nBuild FAILED.\n";

        var result = BuildOutputParser.Parse(output);

        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void TruncatesToMaxDiagnosticsAndFlagsIt()
    {
        var lines = Enumerable.Range(1, 30)
            .Select(i => $@"C:\repo\F{i}.cs({i},1): error CS0103: problem {i} [C:\repo\P.csproj]");
        var output = string.Join("\n", lines) + "\nBuild FAILED.\n";

        var result = BuildOutputParser.Parse(output, maxDiagnostics: 20);

        Assert.Equal(20, result.Diagnostics.Count);
        Assert.True(result.Truncated);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~BuildOutputParserTests"`
Expected: FAIL — `BuildOutputParser` does not exist.

- [x] **Step 3: Write the minimal implementation**

`tools/DialogEditor.UiaMcp.Core/BuildOutputParser.cs`:

```csharp
using System.Text.RegularExpressions;

namespace DialogEditor.UiaMcp.Core;

public record Diagnostic(string Severity, string File, int Line, int Column, string Code, string Message);

public record BuildResult(
    bool Succeeded,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<Diagnostic> Diagnostics,
    bool Truncated);

/// <summary>
/// Turns `dotnet build` console output into a compact result. Raw output is far too
/// noisy to hand back verbatim: a clean Debug build of this solution still emits
/// eight warnings spread over dozens of lines, each repeated per target framework.
/// </summary>
public static class BuildOutputParser
{
    private static readonly Regex DiagnosticPattern = new(
        @"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s+(?<sev>error|warning)\s+(?<code>[A-Z]+\d+):\s+(?<msg>.*?)(?:\s+\[[^\]]*\])?$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex SummaryPattern = new(
        @"^\s*(?<n>\d+)\s+(?<kind>Warning|Error)\(s\)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static BuildResult Parse(string output, int maxDiagnostics = 20)
    {
        var all = new List<Diagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in DiagnosticPattern.Matches(output))
        {
            var key = m.Value;
            // MSBuild repeats the same diagnostic once per target framework.
            if (!seen.Add(key)) continue;

            all.Add(new Diagnostic(
                m.Groups["sev"].Value,
                m.Groups["file"].Value.Trim(),
                int.Parse(m.Groups["line"].Value),
                int.Parse(m.Groups["col"].Value),
                m.Groups["code"].Value,
                m.Groups["msg"].Value.Trim()));
        }

        var errorCount = 0;
        var warningCount = 0;
        foreach (Match m in SummaryPattern.Matches(output))
        {
            var n = int.Parse(m.Groups["n"].Value);
            if (m.Groups["kind"].Value == "Error") errorCount = n; else warningCount = n;
        }

        var ordered = all
            .OrderBy(d => d.Severity == "error" ? 0 : 1)
            .ThenBy(d => d.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Line)
            .ToList();

        var succeeded = output.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase)
                        && !output.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase);

        return new BuildResult(
            succeeded,
            errorCount,
            warningCount,
            ordered.Take(maxDiagnostics).ToList(),
            Truncated: ordered.Count > maxDiagnostics);
    }
}
```

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~BuildOutputParserTests"`
Expected: PASS, 6 tests.

- [x] **Step 5: Commit**

```bash
git add tools/DialogEditor.UiaMcp.Core/BuildOutputParser.cs DialogEditor.Tests/UiaMcp/BuildOutputParserTests.cs
git commit -m "feat(uiamcp): parse dotnet build output into a compact result"
```

---

### Task 3: `TestOutputParser`

**Files:**
- Create: `tools/DialogEditor.UiaMcp.Core/TestOutputParser.cs`
- Test: `DialogEditor.Tests/UiaMcp/TestOutputParserTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `record TestFailure(string TestName, string Message)`; `record TestRunResult(bool Succeeded, int Passed, int Failed, int Skipped, int Total, IReadOnlyList<TestFailure> Failures, bool Truncated)`; `static TestRunResult TestOutputParser.Parse(string output, int maxFailures = 20)`.

- [x] **Step 1: Write the failing tests**

`DialogEditor.Tests/UiaMcp/TestOutputParserTests.cs`:

```csharp
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

public class TestOutputParserTests
{
    [Fact]
    public void ParsesAPassingRunSummary()
    {
        const string output =
            "Passed!  - Failed:     0, Passed:   219, Skipped:     0, Total:   219, Duration: 4 s\n";

        var result = TestOutputParser.Parse(output);

        Assert.True(result.Succeeded);
        Assert.Equal(219, result.Passed);
        Assert.Equal(0, result.Failed);
        Assert.Equal(219, result.Total);
    }

    [Fact]
    public void ParsesAFailingRunSummary()
    {
        const string output =
            "Failed!  - Failed:     3, Passed:   216, Skipped:     1, Total:   220, Duration: 5 s\n";

        var result = TestOutputParser.Parse(output);

        Assert.False(result.Succeeded);
        Assert.Equal(3, result.Failed);
        Assert.Equal(216, result.Passed);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void ExtractsFailingTestNamesAndAssertionMessages()
    {
        const string output = """
              Failed DialogEditor.Tests.UiaMcp.SettingsGuardTests.RestoreCopiesBackupBackAndRemovesIt [12 ms]
              Error Message:
               Assert.True() Failure
              Expected: True
              Actual:   False
              Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 12 ms
            """;

        var result = TestOutputParser.Parse(output);

        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            "DialogEditor.Tests.UiaMcp.SettingsGuardTests.RestoreCopiesBackupBackAndRemovesIt",
            failure.TestName);
        Assert.Contains("Assert.True() Failure", failure.Message);
    }

    [Fact]
    public void TruncatesFailureListAndFlagsIt()
    {
        var lines = Enumerable.Range(1, 25).Select(i =>
            $"  Failed Some.Namespace.Test{i} [1 ms]\n  Error Message:\n   boom {i}");
        var output = string.Join("\n", lines) +
                     "\nFailed!  - Failed:    25, Passed:     0, Skipped:     0, Total:    25\n";

        var result = TestOutputParser.Parse(output, maxFailures: 20);

        Assert.Equal(20, result.Failures.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void ReportsNoSummaryAsAFailedRunRatherThanASilentPass()
    {
        // A crashed runner prints no summary line. Treating that as success would be
        // the worst possible default — it would report green for a run that never ran.
        var result = TestOutputParser.Parse("MSBUILD : error MSB1009: Project file does not exist.\n");

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Total);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~TestOutputParserTests"`
Expected: FAIL — `TestOutputParser` does not exist.

- [x] **Step 3: Write the minimal implementation**

`tools/DialogEditor.UiaMcp.Core/TestOutputParser.cs`:

```csharp
using System.Text.RegularExpressions;

namespace DialogEditor.UiaMcp.Core;

public record TestFailure(string TestName, string Message);

public record TestRunResult(
    bool Succeeded,
    int Passed,
    int Failed,
    int Skipped,
    int Total,
    IReadOnlyList<TestFailure> Failures,
    bool Truncated);

/// <summary>
/// Turns `dotnet test` console output into counts plus named failures. The suite has
/// 219 test files, so raw output is unusable as a tool result.
/// </summary>
public static class TestOutputParser
{
    private static readonly Regex SummaryPattern = new(
        @"(?<verdict>Passed|Failed)!\s+-\s+Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+),\s*Total:\s*(?<total>\d+)",
        RegexOptions.Compiled);

    private static readonly Regex FailurePattern = new(
        @"^\s*Failed\s+(?<name>[^\s\[]+).*?$(?<body>(?:\n(?!\s*(?:Failed|Passed)\s).*)*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static TestRunResult Parse(string output, int maxFailures = 20)
    {
        var failures = new List<TestFailure>();
        foreach (Match m in FailurePattern.Matches(output))
        {
            var name = m.Groups["name"].Value.Trim();
            if (name.Length == 0) continue;
            failures.Add(new TestFailure(name, m.Groups["body"].Value.Trim()));
        }

        var summary = SummaryPattern.Match(output);
        if (!summary.Success)
        {
            // No summary line means the runner never completed. Never report that green.
            return new TestRunResult(false, 0, 0, 0, 0,
                failures.Take(maxFailures).ToList(), failures.Count > maxFailures);
        }

        return new TestRunResult(
            Succeeded: summary.Groups["verdict"].Value == "Passed",
            Passed: int.Parse(summary.Groups["passed"].Value),
            Failed: int.Parse(summary.Groups["failed"].Value),
            Skipped: int.Parse(summary.Groups["skipped"].Value),
            Total: int.Parse(summary.Groups["total"].Value),
            Failures: failures.Take(maxFailures).ToList(),
            Truncated: failures.Count > maxFailures);
    }
}
```

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~TestOutputParserTests"`
Expected: PASS, 5 tests.

- [x] **Step 5: Commit**

```bash
git add tools/DialogEditor.UiaMcp.Core/TestOutputParser.cs DialogEditor.Tests/UiaMcp/TestOutputParserTests.cs
git commit -m "feat(uiamcp): parse dotnet test output into counts and named failures"
```

---

### Task 4: `ElementInfo`, `IUiaTree`, and the audit-seeded fake tree

**Files:**
- Create: `tools/DialogEditor.UiaMcp.Core/ElementInfo.cs`
- Create: `tools/DialogEditor.UiaMcp.Core/IUiaTree.cs`
- Create: `DialogEditor.Tests/UiaMcp/FakeUiaTree.cs`
- Test: `DialogEditor.Tests/UiaMcp/FakeUiaTreeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `record ElementInfo(string Id, string Name, string ControlType, string AutomationId, string ClassName, bool IsEnabled, bool IsOffscreen, bool IsFocusable, IReadOnlyList<string> Patterns)`; `interface IUiaTree { ElementInfo Root { get; } IReadOnlyList<ElementInfo> ChildrenOf(string id); }`; `FakeUiaTree.AuditSnapshot()` returning an `IUiaTree`.

- [x] **Step 1: Write `ElementInfo` and `IUiaTree`**

`tools/DialogEditor.UiaMcp.Core/ElementInfo.cs`:

```csharp
namespace DialogEditor.UiaMcp.Core;

/// <summary>
/// A UIA element flattened to plain data. Deliberately free of any UI Automation type
/// so this assembly stays net8.0 and unit-testable from DialogEditor.Tests.
/// <paramref name="Id"/> is a tree-local identity assigned by the IUiaTree implementation.
/// </summary>
public record ElementInfo(
    string Id,
    string Name,
    string ControlType,
    string AutomationId,
    string ClassName,
    bool IsEnabled,
    bool IsOffscreen,
    bool IsFocusable,
    IReadOnlyList<string> Patterns);
```

`tools/DialogEditor.UiaMcp.Core/IUiaTree.cs`:

```csharp
namespace DialogEditor.UiaMcp.Core;

/// <summary>The seam between addressing logic and live UI Automation.</summary>
public interface IUiaTree
{
    ElementInfo Root { get; }
    IReadOnlyList<ElementInfo> ChildrenOf(string id);
}
```

- [x] **Step 2: Write the failing test for the fake**

`DialogEditor.Tests/UiaMcp/FakeUiaTreeTests.cs`:

```csharp
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

/// <summary>
/// Guards the fixture itself. The resolver is only trustworthy if the tree it is
/// tested against reproduces the ambiguity the real app actually has, so these
/// assertions pin the fake to the 2026-09-03 audit's measured findings
/// (docs/2026-09-03-uia-addressability-audit.md).
/// </summary>
public class FakeUiaTreeTests
{
    private static List<ElementInfo> Flatten(IUiaTree tree, ElementInfo? from = null)
    {
        var node = from ?? tree.Root;
        var all = new List<ElementInfo> { node };
        foreach (var child in tree.ChildrenOf(node.Id))
            all.AddRange(Flatten(tree, child));
        return all;
    }

    [Fact]
    public void ReproducesTheSixViewboxNamedPaneChromeButtons()
    {
        var all = Flatten(FakeUiaTree.AuditSnapshot());
        Assert.Equal(6, all.Count(e => e.Name == "Avalonia.Controls.Viewbox"));
    }

    [Fact]
    public void ReproducesTheLanguageLabelAndComboBoxSharingAName()
    {
        var all = Flatten(FakeUiaTree.AuditSnapshot());
        var language = all.Where(e => e.Name == "Language:").ToList();
        Assert.Equal(2, language.Count);
        Assert.Contains(language, e => e.ControlType == "Text");
        Assert.Contains(language, e => e.ControlType == "ComboBox");
    }

    [Fact]
    public void ReproducesThirtySevenNamelessPatternlessTreeItems()
    {
        var all = Flatten(FakeUiaTree.AuditSnapshot());
        var items = all.Where(e => e.ControlType == "TreeItem").ToList();
        Assert.Equal(37, items.Count);
        Assert.All(items, i => Assert.Equal("", i.Name));
        Assert.All(items, i => Assert.Empty(i.Patterns));
    }

    [Fact]
    public void ReproducesMenuItemsWithNoAutomationId()
    {
        var all = Flatten(FakeUiaTree.AuditSnapshot());
        var menuItems = all.Where(e => e.ControlType == "MenuItem").ToList();
        Assert.NotEmpty(menuItems);
        Assert.All(menuItems, m => Assert.Equal("", m.AutomationId));
    }

    [Fact]
    public void ReproducesNamedPanes()
    {
        var all = Flatten(FakeUiaTree.AuditSnapshot());
        Assert.Contains(all, e => e is { ControlType: "Pane", Name: "Conversations", AutomationId: "LeftPane" });
        Assert.Contains(all, e => e is { ControlType: "Pane", Name: "Node Details", AutomationId: "RightPane" });
    }
}
```

- [x] **Step 3: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~FakeUiaTreeTests"`
Expected: FAIL — `FakeUiaTree` does not exist.

- [x] **Step 4: Write the fake**

`DialogEditor.Tests/UiaMcp/FakeUiaTree.cs`:

```csharp
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

/// <summary>
/// In-memory IUiaTree. AuditSnapshot() reproduces the project-open surface measured on
/// 2026-09-03 — including its collisions — so resolver rules are exercised against the
/// real shape of this app's ambiguity rather than invented examples.
/// </summary>
public sealed class FakeUiaTree : IUiaTree
{
    private readonly Dictionary<string, List<ElementInfo>> _children = new();
    public ElementInfo Root { get; private set; } = null!;

    public IReadOnlyList<ElementInfo> ChildrenOf(string id) =>
        _children.TryGetValue(id, out var kids) ? kids : Array.Empty<ElementInfo>();

    private ElementInfo Add(string? parentId, string name, string controlType,
        string automationId = "", string className = "", bool enabled = true,
        bool offscreen = false, bool focusable = false, params string[] patterns)
    {
        var el = new ElementInfo($"e{_children.Count}_{Guid.NewGuid():N}", name, controlType,
            automationId, className, enabled, offscreen, focusable, patterns);
        _children[el.Id] = new List<ElementInfo>();
        if (parentId is null) Root = el;
        else _children[parentId].Add(el);
        return el;
    }

    public static FakeUiaTree AuditSnapshot()
    {
        var t = new FakeUiaTree();
        var win = t.Add(null, "Pillars Dialog Editor [AuditScratch]", "Window", className: "MainWindow");

        // Title bar: the OS system menu is a MenuItem peer of the app's own menus.
        var titleBar = t.Add(win.Id, "Pillars Dialog Editor [AuditScratch]", "TitleBar", "TitleBar");
        var sysBar = t.Add(titleBar.Id, "System Menu Bar", "MenuBar", "SystemMenuBar");
        t.Add(sysBar.Id, "System", "MenuItem", "Item 1");

        // The app's own menu is ANONYMOUS — selectable only by ClassName.
        var menu = t.Add(win.Id, "", "Menu", className: "Menu");
        foreach (var top in new[] { "File", "Edit", "View", "Test", "Help" })
        {
            var mi = t.Add(menu.Id, top, "MenuItem", className: "MenuItem");
            if (top == "File")
                foreach (var item in new[] { "New Project…", "Open Project…", "Save Project", "Close Project" })
                    t.Add(mi.Id, item, "MenuItem", className: "MenuItem");
            if (top == "Edit") { t.Add(mi.Id, "↩", "MenuItem"); t.Add(mi.Id, "↪", "MenuItem"); }
        }

        // Label and its ComboBox share a Name.
        t.Add(win.Id, "Language:", "Text", className: "TextBlock");
        t.Add(win.Id, "Language:", "ComboBox", className: "ComboBox", focusable: true,
            patterns: new[] { "ExpandCollapse", "Value" });

        var dockHost = t.Add(win.Id, "Dock host", "Pane", className: "DockControl");
        var rootDock = t.Add(dockHost.Id, "Root dock", "Pane", className: "RootDockControl");

        var left = t.Add(rootDock.Id, "Conversations", "Pane", "LeftPane", "ToolControl");
        AddPaneChrome(t, rootDock.Id);

        // 37 nameless, patternless conversation rows.
        var tree = t.Add(left.Id, "", "Tree", className: "TreeView");
        for (var i = 0; i < 37; i++)
        {
            var item = t.Add(tree.Id, "", "TreeItem", focusable: true);
            t.Add(item.Id, "", "Button", "PART_ExpandCollapseChevron");
            t.Add(item.Id, i == 0 ? "(root)" : $"{i:00}_conversation", "Text");
        }

        var docs = t.Add(rootDock.Id, "Canvas", "Pane", "Documents", "DocumentControl");
        var docTabs = t.Add(docs.Id, "Document tabs", "Tab", "Documents", "DocumentTabStrip");
        t.Add(docTabs.Id, "Canvas", "TabItem", "Canvas");

        var right = t.Add(rootDock.Id, "Node Details", "Pane", "RightPane", "ToolControl");
        AddPaneChrome(t, rootDock.Id);
        var toolTabs = t.Add(right.Id, "Tool tabs", "Tab", "RightPane", "ToolTabStrip");
        t.Add(toolTabs.Id, "Node Details", "TabItem", "Details");
        t.Add(toolTabs.Id, "Condition search", "TabItem", "ConditionSearch");

        t.Add(win.Id, "Opened project 'AuditScratch' (0 patches)", "Text", "StatusLiveRegion");
        return t;
    }

    // Three Viewbox-named buttons per ToolControl pane, ids duplicated across panes.
    private static void AddPaneChrome(FakeUiaTree t, string parentId)
    {
        foreach (var part in new[] { "PART_MenuButton", "PART_PinButton", "PART_CloseButton" })
            t.Add(parentId, "Avalonia.Controls.Viewbox", "Button", part, "Button",
                focusable: true, patterns: new[] { "Invoke" });
    }
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~FakeUiaTreeTests"`
Expected: PASS, 6 tests. (The planned assertion that ALL MenuItems lack an AutomationId was wrong: the OS system menu item carries 'Item 1'. Split into an app-menu test and a test pinning the OS peer — audit findings 4 and 5.)

- [x] **Step 6: Commit**

```bash
git add tools/DialogEditor.UiaMcp.Core/ElementInfo.cs tools/DialogEditor.UiaMcp.Core/IUiaTree.cs DialogEditor.Tests/UiaMcp/FakeUiaTree.cs DialogEditor.Tests/UiaMcp/FakeUiaTreeTests.cs
git commit -m "test(uiamcp): add IUiaTree seam and audit-seeded fake tree"
```

---

### Task 5: `Selector` and `RefTable`

**Files:**
- Create: `tools/DialogEditor.UiaMcp.Core/Selector.cs`
- Create: `tools/DialogEditor.UiaMcp.Core/RefTable.cs`
- Test: `DialogEditor.Tests/UiaMcp/RefTableTests.cs`

**Interfaces:**
- Consumes: `ElementInfo` (Task 4).
- Produces: `record Selector(string? Name, string? ControlType, string? AutomationId, string? WithinPane, int? Nth)`; `class RefTable` with `int Generation`, `IReadOnlyList<string> Mint(IEnumerable<ElementInfo>)`, `bool TryResolve(string reference, out string elementId, out string error)`.

- [ ] **Step 1: Write `Selector`**

`tools/DialogEditor.UiaMcp.Core/Selector.cs`:

```csharp
namespace DialogEditor.UiaMcp.Core;

/// <summary>
/// An address for an element. Every criterion supplied is ANDed. Nth is the only way
/// to accept an ambiguous match — the resolver otherwise errors rather than guessing.
/// </summary>
public record Selector(
    string? Name = null,
    string? ControlType = null,
    string? AutomationId = null,
    string? WithinPane = null,
    int? Nth = null)
{
    public bool IsEmpty => Name is null && ControlType is null && AutomationId is null;
}
```

- [ ] **Step 2: Write the failing tests for `RefTable`**

`DialogEditor.Tests/UiaMcp/RefTableTests.cs`:

```csharp
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

public class RefTableTests
{
    private static ElementInfo El(string id, string name) =>
        new(id, name, "Button", "", "", true, false, true, new[] { "Invoke" });

    [Fact]
    public void MintReturnsSequentialReferences()
    {
        var table = new RefTable();
        var refs = table.Mint(new[] { El("a", "One"), El("b", "Two") });
        Assert.Equal(new[] { "ref_1", "ref_2" }, refs);
    }

    [Fact]
    public void ResolveReturnsTheElementIdForAMintedReference()
    {
        var table = new RefTable();
        table.Mint(new[] { El("element-a", "One") });

        Assert.True(table.TryResolve("ref_1", out var id, out _));
        Assert.Equal("element-a", id);
    }

    [Fact]
    public void MintingAgainStartsANewGenerationAndInvalidatesOldReferences()
    {
        var table = new RefTable();
        table.Mint(new[] { El("element-a", "One") });
        table.Mint(new[] { El("element-b", "Two") });

        Assert.False(table.TryResolve("ref_1@1", out _, out var error));
        Assert.Contains("stale", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("read_tree", error);
    }

    [Fact]
    public void GenerationIncrementsOnEachMint()
    {
        var table = new RefTable();
        Assert.Equal(0, table.Generation);
        table.Mint(new[] { El("a", "One") });
        Assert.Equal(1, table.Generation);
    }

    [Fact]
    public void UnknownReferenceIsRejectedWithAHelpfulMessage()
    {
        var table = new RefTable();
        table.Mint(new[] { El("a", "One") });

        Assert.False(table.TryResolve("ref_99", out _, out var error));
        Assert.Contains("ref_99", error);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~RefTableTests"`
Expected: FAIL — `RefTable` does not exist.

- [ ] **Step 4: Write the minimal implementation**

`tools/DialogEditor.UiaMcp.Core/RefTable.cs`:

```csharp
namespace DialogEditor.UiaMcp.Core;

/// <summary>
/// Maps stable "ref_N" handles to tree-local element ids. Each read_tree call starts a
/// new generation; a handle carrying an older generation is rejected rather than
/// silently resolved against a tree that has since changed.
/// </summary>
public sealed class RefTable
{
    private readonly Dictionary<string, string> _byRef = new(StringComparer.Ordinal);

    public int Generation { get; private set; }

    public IReadOnlyList<string> Mint(IEnumerable<ElementInfo> elements)
    {
        Generation++;
        _byRef.Clear();

        var refs = new List<string>();
        var n = 0;
        foreach (var el in elements)
        {
            var handle = $"ref_{++n}";
            _byRef[handle] = el.Id;
            refs.Add(handle);
        }
        return refs;
    }

    public bool TryResolve(string reference, out string elementId, out string error)
    {
        elementId = "";
        error = "";

        // Callers may echo back a generation-qualified handle, e.g. "ref_3@2".
        var handle = reference;
        var at = reference.IndexOf('@');
        if (at >= 0)
        {
            handle = reference[..at];
            if (!int.TryParse(reference[(at + 1)..], out var gen) || gen != Generation)
            {
                error = $"Stale ref '{reference}': the tree has changed since it was minted " +
                        $"(held generation {reference[(at + 1)..]}, current {Generation}). " +
                        "Re-run read_tree and use a fresh ref.";
                return false;
            }
        }

        if (_byRef.TryGetValue(handle, out var id))
        {
            elementId = id;
            return true;
        }

        error = $"Unknown ref '{reference}'. Re-run read_tree to mint refs for the current tree.";
        return false;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~RefTableTests"`
Expected: PASS, 5 tests.

- [ ] **Step 6: Commit**

```bash
git add tools/DialogEditor.UiaMcp.Core/Selector.cs tools/DialogEditor.UiaMcp.Core/RefTable.cs DialogEditor.Tests/UiaMcp/RefTableTests.cs
git commit -m "feat(uiamcp): add Selector and generation-aware RefTable"
```

---

### Task 6: `Resolver` — the ambiguity contract

This is the core of the design: the departure from `Invoke-ElementClick`'s silent first-match.

**Files:**
- Create: `tools/DialogEditor.UiaMcp.Core/Resolver.cs`
- Test: `DialogEditor.Tests/UiaMcp/ResolverTests.cs`

**Interfaces:**
- Consumes: `IUiaTree`, `ElementInfo`, `Selector` (Tasks 4–5).
- Produces: `record ResolveResult(ElementInfo? Element, string? ErrorKind, string? ErrorMessage)`; `class Resolver(IUiaTree tree, string? uiLanguage = null)` with `ResolveResult Resolve(Selector selector)` and `IReadOnlyList<ElementInfo> Flatten(string? withinPane = null)`.

- [ ] **Step 1: Write the failing tests**

`DialogEditor.Tests/UiaMcp/ResolverTests.cs`:

```csharp
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

public class ResolverTests
{
    private static Resolver NewResolver() => new(FakeUiaTree.AuditSnapshot());

    [Fact]
    public void ResolvesAnUnambiguousNameMatch()
    {
        var result = NewResolver().Resolve(new Selector(Name: "Conversations"));

        Assert.Null(result.ErrorKind);
        Assert.Equal("Pane", result.Element!.ControlType);
    }

    [Fact]
    public void ErrorsAsAmbiguousRatherThanTakingTheFirstMatch()
    {
        // Six pane-chrome buttons share this name. DriveApp's FindFirst would have
        // silently clicked whichever came first in tree order.
        var result = NewResolver().Resolve(new Selector(Name: "Avalonia.Controls.Viewbox"));

        Assert.Equal("Ambiguous", result.ErrorKind);
        Assert.Null(result.Element);
        Assert.Contains("6", result.ErrorMessage);
    }

    [Fact]
    public void AmbiguityErrorListsTheCandidatesDistinguishingFields()
    {
        var result = NewResolver().Resolve(new Selector(Name: "Avalonia.Controls.Viewbox"));

        Assert.Contains("PART_MenuButton", result.ErrorMessage);
        Assert.Contains("PART_CloseButton", result.ErrorMessage);
    }

    [Fact]
    public void ControlTypeDisambiguatesTheLanguageLabelFromItsComboBox()
    {
        var result = NewResolver().Resolve(new Selector(Name: "Language:", ControlType: "ComboBox"));

        Assert.Null(result.ErrorKind);
        Assert.Equal("ComboBox", result.Element!.ControlType);
    }

    [Fact]
    public void WithinPaneScopesTheSearchToThatPanesSubtree()
    {
        var result = NewResolver().Resolve(
            new Selector(Name: "Node Details", ControlType: "TabItem", WithinPane: "Node Details"));

        Assert.Null(result.ErrorKind);
        Assert.Equal("Details", result.Element!.AutomationId);
    }

    [Fact]
    public void NthSelectsExplicitlyAmongAmbiguousMatches()
    {
        var result = NewResolver().Resolve(new Selector(Name: "Avalonia.Controls.Viewbox", Nth: 1));

        Assert.Null(result.ErrorKind);
        Assert.Equal("PART_PinButton", result.Element!.AutomationId);
    }

    [Fact]
    public void NotFoundErrorOffersNearMisses()
    {
        // A plausible mistake: dropping the ellipsis character from a menu label.
        var result = NewResolver().Resolve(new Selector(Name: "Open Project"));

        Assert.Equal("NotFound", result.ErrorKind);
        Assert.Contains("Open Project…", result.ErrorMessage);
    }

    [Fact]
    public void NotFoundErrorNamesTheActiveUiLanguage()
    {
        // Audit finding 4: menu items have no AutomationId, so every lookup is coupled
        // to localised label text. Without the language, "not found" is ambiguous
        // between renamed, translated, and wrong-surface-open.
        var resolver = new Resolver(FakeUiaTree.AuditSnapshot(), uiLanguage: "en");

        var result = resolver.Resolve(new Selector(Name: "Definitely Not Present"));

        Assert.Equal("NotFound", result.ErrorKind);
        Assert.Contains("en", result.ErrorMessage);
    }

    [Fact]
    public void NotFoundErrorListsTheOpenPanes()
    {
        var result = NewResolver().Resolve(new Selector(Name: "Definitely Not Present"));

        Assert.Equal("NotFound", result.ErrorKind);
        Assert.Contains("Conversations", result.ErrorMessage);
        Assert.Contains("Node Details", result.ErrorMessage);
    }

    [Fact]
    public void UnknownPaneIsReportedAsNotFoundNamingThePane()
    {
        var result = NewResolver().Resolve(new Selector(Name: "Canvas", WithinPane: "No Such Pane"));

        Assert.Equal("NotFound", result.ErrorKind);
        Assert.Contains("No Such Pane", result.ErrorMessage);
    }

    [Fact]
    public void FlattenScopedToAPaneExcludesOtherPanesContents()
    {
        var all = NewResolver().Flatten(withinPane: "Node Details");
        Assert.DoesNotContain(all, e => e.ControlType == "TreeItem");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~ResolverTests"`
Expected: FAIL — `Resolver` does not exist.

- [ ] **Step 3: Write the minimal implementation**

`tools/DialogEditor.UiaMcp.Core/Resolver.cs`:

```csharp
using System.Text;

namespace DialogEditor.UiaMcp.Core;

public record ResolveResult(ElementInfo? Element, string? ErrorKind, string? ErrorMessage)
{
    public static ResolveResult Ok(ElementInfo el) => new(el, null, null);
    public static ResolveResult Error(string kind, string message) => new(null, kind, message);
}

/// <summary>
/// Turns a Selector into exactly one element, or into an actionable error.
///
/// The contract that matters: MORE THAN ONE MATCH IS AN ERROR. DriveApp.ps1 resolves
/// with FindFirst on Name and takes tree order, and the 2026-09-03 audit found eight
/// same-surface name collisions — so that approach can click the wrong control and
/// still report success. Only an explicit Nth accepts an ambiguous match.
/// </summary>
public sealed class Resolver(IUiaTree tree, string? uiLanguage = null)
{
    private const int MaxNearMisses = 10;
    private const int MaxEditDistance = 3;

    public IReadOnlyList<ElementInfo> Flatten(string? withinPane = null)
    {
        var start = tree.Root;
        if (withinPane is not null)
        {
            var pane = FindPane(withinPane);
            if (pane is null) return Array.Empty<ElementInfo>();
            start = pane;
        }

        var all = new List<ElementInfo>();
        Walk(start, all);
        return all;
    }

    private void Walk(ElementInfo node, List<ElementInfo> into)
    {
        into.Add(node);
        foreach (var child in tree.ChildrenOf(node.Id)) Walk(child, into);
    }

    private ElementInfo? FindPane(string paneNameOrId)
    {
        var all = new List<ElementInfo>();
        Walk(tree.Root, all);
        return all.FirstOrDefault(e => e.ControlType == "Pane" &&
            (string.Equals(e.Name, paneNameOrId, StringComparison.Ordinal) ||
             string.Equals(e.AutomationId, paneNameOrId, StringComparison.Ordinal)));
    }

    public ResolveResult Resolve(Selector selector)
    {
        if (selector.WithinPane is not null && FindPane(selector.WithinPane) is null)
        {
            return ResolveResult.Error("NotFound",
                $"No pane named or identified as '{selector.WithinPane}'. Open panes: {OpenPanes()}.");
        }

        var candidates = Flatten(selector.WithinPane).Where(e => Matches(e, selector)).ToList();

        if (candidates.Count == 1) return ResolveResult.Ok(candidates[0]);

        if (candidates.Count > 1)
        {
            if (selector.Nth is { } n)
            {
                if (n >= 0 && n < candidates.Count) return ResolveResult.Ok(candidates[n]);
                return ResolveResult.Error("NotFound",
                    $"nth={n} is out of range: only {candidates.Count} elements matched.");
            }
            return ResolveResult.Error("Ambiguous", DescribeAmbiguity(candidates));
        }

        return ResolveResult.Error("NotFound", DescribeNotFound(selector));
    }

    private static bool Matches(ElementInfo e, Selector s) =>
        (s.Name is null || string.Equals(e.Name, s.Name, StringComparison.Ordinal)) &&
        (s.ControlType is null || string.Equals(e.ControlType, s.ControlType, StringComparison.Ordinal)) &&
        (s.AutomationId is null || string.Equals(e.AutomationId, s.AutomationId, StringComparison.Ordinal));

    private string OpenPanes() =>
        string.Join(", ", Flatten()
            .Where(e => e.ControlType == "Pane" && e.Name.Length > 0)
            .Select(e => $"'{e.Name}'")
            .Distinct());

    private static string DescribeAmbiguity(List<ElementInfo> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{candidates.Count} elements matched; refusing to guess which one you meant.");
        sb.AppendLine("Narrow with controlType, automationId, withinPane, or nth. Candidates:");
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            sb.AppendLine($"  nth={i}: [{c.ControlType}] name='{c.Name}' automationId='{c.AutomationId}' class='{c.ClassName}'");
        }
        return sb.ToString();
    }

    private string DescribeNotFound(Selector selector)
    {
        var sb = new StringBuilder();
        var scope = selector.WithinPane is null ? "the window" : $"pane '{selector.WithinPane}'";
        sb.AppendLine($"No element matched in {scope}.");

        if (selector.Name is { } wanted)
        {
            var misses = NearMisses(wanted, selector.WithinPane);
            if (misses.Count > 0)
            {
                sb.AppendLine("Did you mean one of these? (note that menu labels use the ellipsis character '…', not three dots)");
                foreach (var m in misses)
                    sb.AppendLine($"  [{m.ControlType}] '{m.Name}' automationId='{m.AutomationId}'");
            }
        }

        sb.AppendLine($"Open panes: {OpenPanes()}.");
        if (uiLanguage is not null)
            sb.AppendLine($"Active UI language: '{uiLanguage}' — names are matched against " +
                          "the localised text currently shown, so a locale change moves them.");
        return sb.ToString();
    }

    private List<ElementInfo> NearMisses(string wanted, string? withinPane)
    {
        var named = Flatten(withinPane).Where(e => e.Name.Length > 0).ToList();

        var substring = named
            .Where(e => e.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase) ||
                        wanted.Contains(e.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var close = named
            .Except(substring)
            .Where(e => EditDistance(e.Name, wanted) <= MaxEditDistance)
            .ToList();

        return substring.Concat(close).Take(MaxNearMisses).ToList();
    }

    private static int EditDistance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~ResolverTests"`
Expected: PASS, 11 tests.

- [ ] **Step 5: Commit**

```bash
git add tools/DialogEditor.UiaMcp.Core/Resolver.cs DialogEditor.Tests/UiaMcp/ResolverTests.cs
git commit -m "feat(uiamcp): resolve selectors, erroring on ambiguity instead of guessing"
```

---

### Task 7: `AddressabilityWarnings`

Makes the server a detector for issue #15 rather than a workaround for it.

**Files:**
- Create: `tools/DialogEditor.UiaMcp.Core/AddressabilityWarnings.cs`
- Test: `DialogEditor.Tests/UiaMcp/AddressabilityWarningsTests.cs`

**Interfaces:**
- Consumes: `ElementInfo` (Task 4).
- Produces: `record AddressabilityWarning(string Kind, string Detail)`; `static IReadOnlyList<AddressabilityWarning> AddressabilityWarnings.Inspect(IReadOnlyList<ElementInfo> elements)`.

- [ ] **Step 1: Write the failing tests**

`DialogEditor.Tests/UiaMcp/AddressabilityWarningsTests.cs`:

```csharp
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

public class AddressabilityWarningsTests
{
    private static IReadOnlyList<ElementInfo> AuditElements() =>
        new Resolver(FakeUiaTree.AuditSnapshot()).Flatten();

    [Fact]
    public void FlagsCollidingNamesWithinTheScope()
    {
        var warnings = AddressabilityWarnings.Inspect(AuditElements());

        var collision = Assert.Single(warnings.Where(
            w => w.Kind == "CollidingName" && w.Detail.Contains("Avalonia.Controls.Viewbox")));
        Assert.Contains("6", collision.Detail);
    }

    [Fact]
    public void FlagsTypeNameLeaksInAccessibleNames()
    {
        var warnings = AddressabilityWarnings.Inspect(AuditElements());
        Assert.Contains(warnings, w => w.Kind == "TypeNameLeak");
    }

    [Fact]
    public void FlagsFocusableElementsThatHaveNoName()
    {
        var warnings = AddressabilityWarnings.Inspect(AuditElements());

        var unnamed = Assert.Single(warnings.Where(
            w => w.Kind == "UnnamedFocusable" && w.Detail.Contains("TreeItem")));
        Assert.Contains("37", unnamed.Detail);
    }

    [Fact]
    public void FlagsFocusableElementsExposingNoPatterns()
    {
        var warnings = AddressabilityWarnings.Inspect(AuditElements());
        Assert.Contains(warnings, w => w.Kind == "NoPatterns" && w.Detail.Contains("TreeItem"));
    }

    [Fact]
    public void ReturnsNothingForACleanTree()
    {
        var clean = new List<ElementInfo>
        {
            new("a", "Save", "Button", "SaveButton", "Button", true, false, true, new[] { "Invoke" }),
            new("b", "Open", "Button", "OpenButton", "Button", true, false, true, new[] { "Invoke" }),
        };

        Assert.Empty(AddressabilityWarnings.Inspect(clean));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~AddressabilityWarningsTests"`
Expected: FAIL — `AddressabilityWarnings` does not exist.

- [ ] **Step 3: Write the minimal implementation**

`tools/DialogEditor.UiaMcp.Core/AddressabilityWarnings.cs`:

```csharp
namespace DialogEditor.UiaMcp.Core;

public record AddressabilityWarning(string Kind, string Detail);

/// <summary>
/// Reports what a scope cannot address, so verification runs surface accessibility
/// defects instead of hiding them.
///
/// This exists because the friction is load-bearing: an element that cannot be found
/// by name is a defect a screen-reader user would hit too. A harness that quietly
/// routed around it would let the app get less accessible while runs got greener.
/// Every warning here corresponds to a finding in
/// docs/2026-09-03-uia-addressability-audit.md.
/// </summary>
public static class AddressabilityWarnings
{
    private static readonly string[] TypeNamePrefixes = { "Avalonia.", "System.Windows.", "System.Controls." };

    public static IReadOnlyList<AddressabilityWarning> Inspect(IReadOnlyList<ElementInfo> elements)
    {
        var warnings = new List<AddressabilityWarning>();

        foreach (var group in elements.Where(e => e.Name.Length > 0)
                     .GroupBy(e => e.Name, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            var ids = string.Join(", ", group.Select(e => $"'{e.AutomationId}'").Distinct());
            var types = string.Join("/", group.Select(e => e.ControlType).Distinct());
            warnings.Add(new AddressabilityWarning("CollidingName",
                $"'{group.Key}' matches {group.Count()} elements ({types}; automationIds {ids}) — " +
                "a bare name lookup here is ambiguous."));
        }

        foreach (var group in elements
                     .Where(e => TypeNamePrefixes.Any(p => e.Name.StartsWith(p, StringComparison.Ordinal)))
                     .GroupBy(e => e.Name, StringComparer.Ordinal))
        {
            warnings.Add(new AddressabilityWarning("TypeNameLeak",
                $"{group.Count()} element(s) expose the framework type name '{group.Key}' as their " +
                "accessible name — meaningless to a screen reader and untranslatable."));
        }

        foreach (var group in elements.Where(e => e.IsFocusable && e.Name.Length == 0)
                     .GroupBy(e => e.ControlType, StringComparer.Ordinal))
        {
            warnings.Add(new AddressabilityWarning("UnnamedFocusable",
                $"{group.Count()} focusable {group.Key} element(s) have no accessible name — " +
                "unreachable by name."));
        }

        foreach (var group in elements.Where(e => e.IsFocusable && e.Patterns.Count == 0)
                     .GroupBy(e => e.ControlType, StringComparer.Ordinal))
        {
            warnings.Add(new AddressabilityWarning("NoPatterns",
                $"{group.Count()} focusable {group.Key} element(s) expose no UI Automation pattern — " +
                "not programmatically operable; only a synthetic click can reach them."));
        }

        return warnings;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~AddressabilityWarningsTests"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Run the whole suite to confirm nothing regressed**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`
Expected: PASS, all pre-existing tests plus the 44 added so far.

- [ ] **Step 6: Commit**

```bash
git add tools/DialogEditor.UiaMcp.Core/AddressabilityWarnings.cs DialogEditor.Tests/UiaMcp/AddressabilityWarningsTests.cs
git commit -m "feat(uiamcp): report unaddressable and ambiguous elements as warnings"
```

---

### Task 8: Server host, session tools, and the live `UiaTree`

First task with live I/O. Verified by running the server, not by unit tests.

**Files:**
- Create: `tools/DialogEditor.UiaMcp/DialogEditor.UiaMcp.csproj`
- Create: `tools/DialogEditor.UiaMcp/Program.cs`
- Create: `tools/DialogEditor.UiaMcp/Win32.cs`
- Create: `tools/DialogEditor.UiaMcp/UiaTree.cs`
- Create: `tools/DialogEditor.UiaMcp/EditorSession.cs`
- Create: `tools/DialogEditor.UiaMcp/Tools/SessionTools.cs`
- Create: `tools/DialogEditor.UiaMcp/drive.ps1`
- Modify: `DialogEditor.slnx`

**Interfaces:**
- Consumes: `SettingsGuard` (Task 1), `ElementInfo`/`IUiaTree` (Task 4).
- Produces: `EditorSession` with `string Launch(string repoRoot, string project)`, `void Foreground()`, `IUiaTree Tree()`, `string Kill()`, `string Status()`; MCP tools `launch_app`, `session_status`, `kill_app`.

- [ ] **Step 1: Create the server project**

`tools/DialogEditor.UiaMcp/DialogEditor.UiaMcp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <!-- net8.0-windows + UseWPF is what supplies UIAutomationClient / UIAutomationTypes,
         the same assemblies DriveApp.ps1 loads with Add-Type. NOTE: UseWPF swaps in the
         WPF implicit-usings set, which does NOT include System.IO — import it explicitly. -->
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" Version="2.2.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.11" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\DialogEditor.UiaMcp.Core\DialogEditor.UiaMcp.Core.csproj" />
  </ItemGroup>
</Project>
```

Add to `DialogEditor.slnx` after the Core project line:

```xml
  <Project Path="tools/DialogEditor.UiaMcp/DialogEditor.UiaMcp.csproj" />
```

- [ ] **Step 2: Write the Win32 shim**

`tools/DialogEditor.UiaMcp/Win32.cs`:

```csharp
using System.Runtime.InteropServices;

namespace DialogEditor.UiaMcp;

/// <summary>
/// Synthetic input and window helpers. A real mouse click is needed because Avalonia's
/// top-level MenuItems implement neither Invoke nor ExpandCollapse.
/// </summary>
internal static class Win32
{
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern void SetCursorPos(int x, int y);
    [DllImport("user32.dll")] internal static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);

    internal static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);   // left down
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);   // left up
    }
}
```

- [ ] **Step 3: Write the live `UiaTree`**

`tools/DialogEditor.UiaMcp/UiaTree.cs`:

```csharp
using System.Windows.Automation;
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp;

/// <summary>
/// The only class that touches live UI Automation. Assigns each element a stable
/// tree-local id on first sight so Core can address elements without ever seeing an
/// AutomationElement.
/// </summary>
internal sealed class UiaTree : IUiaTree
{
    private readonly Dictionary<string, AutomationElement> _elements = new(StringComparer.Ordinal);
    private int _next;

    public ElementInfo Root { get; }

    public UiaTree(AutomationElement root) => Root = Register(root);

    internal AutomationElement Element(string id) =>
        _elements.TryGetValue(id, out var el)
            ? el
            : throw new InvalidOperationException($"Unknown element id '{id}'.");

    private ElementInfo Register(AutomationElement el)
    {
        var id = $"e{++_next}";
        _elements[id] = el;

        var c = el.Current;
        var patterns = el.GetSupportedPatterns()
            .Select(p => p.ProgrammaticName.Replace("Identifiers.", "").Replace("Pattern", ""))
            .ToArray();

        return new ElementInfo(
            id,
            c.Name ?? "",
            (c.ControlType?.ProgrammaticName ?? "").Replace("ControlType.", ""),
            c.AutomationId ?? "",
            c.ClassName ?? "",
            c.IsEnabled,
            c.IsOffscreen,
            c.IsKeyboardFocusable,
            patterns);
    }

    public IReadOnlyList<ElementInfo> ChildrenOf(string id)
    {
        var parent = Element(id);
        var result = new List<ElementInfo>();
        var walker = TreeWalker.ControlViewWalker;
        try
        {
            var child = walker.GetFirstChild(parent);
            while (child is not null)
            {
                result.Add(Register(child));
                child = walker.GetNextSibling(child);
            }
        }
        catch (ElementNotAvailableException)
        {
            // The element vanished mid-walk (a popup closed). A partial child list is
            // the correct answer here; the caller re-reads if it needs a fresh tree.
        }
        return result;
    }
}
```

- [ ] **Step 4: Write `EditorSession`**

`tools/DialogEditor.UiaMcp/EditorSession.cs`:

```csharp
using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp;

/// <summary>
/// Holds the launched app across tool calls. UIA element references stay valid for as
/// long as the owning process lives, so a singleton session is sound.
/// </summary>
internal sealed class EditorSession
{
    private static readonly string DefaultSettings = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PillarsDialogEditor", "settings.json");
    private static readonly string DefaultBackup = Path.Combine(
        Path.GetTempPath(), "PillarsDialogEditor.settings.backup.json");

    private readonly SettingsGuard _guard;
    private Process? _process;
    private UiaTree? _tree;

    public EditorSession() : this(new SettingsGuard(DefaultSettings, DefaultBackup)) { }

    public EditorSession(SettingsGuard guard)
    {
        _guard = guard;
        // Startup net for a previous run that died before restoring.
        StartupRecovery = _guard.RecoverStaleBackup();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SafeRestore();
    }

    public string? StartupRecovery { get; }
    public bool IsLive => _process is { HasExited: false };

    public string Launch(string repoRoot, string project)
    {
        var exe = Path.Combine(repoRoot, "DialogEditor.Avalonia", "bin", "Debug", "net8.0",
                               "DialogEditor.Avalonia.exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException($"NotBuilt: {exe} — run the build tool first.");

        _guard.Backup();
        _guard.SetLastProjectPath(project == "none" ? "" : project);

        _process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true })
                   ?? throw new InvalidOperationException("Process.Start returned null.");

        var window = WaitForWindow(_process, TimeSpan.FromSeconds(30));
        _tree = new UiaTree(window);
        Foreground();
        return _process.MainWindowTitle;
    }

    private static AutomationElement WaitForWindow(Process p, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            p.Refresh();
            if (p.HasExited) throw new InvalidOperationException("AppExited during startup.");

            var win = AutomationElement.RootElement.FindFirst(TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, p.Id));
            if (win is not null) return win;

            Thread.Sleep(250);
        }
        throw new TimeoutException($"No UIA window appeared for PID {p.Id} within {timeout.TotalSeconds:n0}s.");
    }

    public void Foreground()
    {
        if (_process is null) return;
        Win32.SetForegroundWindow(_process.MainWindowHandle);
        Thread.Sleep(400);
    }

    public UiaTree Tree() => _tree
        ?? throw new InvalidOperationException("NoSession: call launch_app first.");

    public string Status()
    {
        if (_process is null) return "no session";
        _process.Refresh();
        return $"pid={_process.Id} exited={_process.HasExited} title='{_process.MainWindowTitle}'";
    }

    /// <summary>Kills the app FIRST — it writes settings on exit and would win the race.</summary>
    public string Kill()
    {
        try
        {
            if (_process is { HasExited: false }) { _process.Kill(); _process.WaitForExit(5000); }
        }
        finally
        {
            _process = null;
            _tree = null;
        }
        return _guard.Restore();
    }

    private void SafeRestore()
    {
        try { _guard.Restore(); }
        catch (Exception ex) { Console.Error.WriteLine($"Settings restore failed on exit: {ex}"); }
    }
}
```

- [ ] **Step 5: Write the session tools and host**

`tools/DialogEditor.UiaMcp/Tools/SessionTools.cs`:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DialogEditor.UiaMcp.Tools;

[McpServerToolType]
internal sealed class SessionTools(EditorSession session)
{
    [McpServerTool, Description("Launch the Dialog Editor and hold the session. Returns the window title.")]
    public string LaunchApp(
        [Description("Absolute path to the repository root")] string repoRoot,
        [Description("'none' for a projectless start, or an absolute .dialogproject path")] string project = "none",
        [Description("Kill an existing session first instead of refusing")] bool force = false)
    {
        if (session.IsLive && !force)
            return "Error(SessionAlreadyLive): an app session is already running. " +
                   "Call kill_app first, or pass force=true.";

        if (session.IsLive) session.Kill();

        var title = session.Launch(repoRoot, project);
        var prefix = session.StartupRecovery is { } r ? r + "\n" : "";
        return $"{prefix}launched: '{title}'";
    }

    [McpServerTool, Description("Report whether a live app session is held, with its pid and window title.")]
    public string SessionStatus() => session.Status();

    [McpServerTool, Description("Kill the app and restore the user's real settings.")]
    public string KillApp() => session.Kill();
}
```

`tools/DialogEditor.UiaMcp/Program.cs`:

```csharp
using DialogEditor.UiaMcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// stdout is the JSON-RPC channel. Every log line MUST go to stderr or it corrupts
// the protocol stream.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<EditorSession>();
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
```

- [ ] **Step 6: Add the manual verification driver**

`tools/DialogEditor.UiaMcp/drive.ps1`:

```powershell
# Manual verification driver for the MCP server. Speaks newline-delimited JSON-RPC
# over stdio, which is how an MCP client talks to it. Use to smoke-test tools
# without wiring the server into a client.
#
#   pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Tool session_status
#   pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Tool launch_app -Arguments @{ repoRoot = (Get-Location).Path }

param(
    [Parameter(Mandatory)][string]$Tool,
    [hashtable]$Arguments = @{},
    [string]$Exe = "tools/DialogEditor.UiaMcp/bin/Debug/net8.0-windows/DialogEditor.UiaMcp.exe",
    [int]$TimeoutSec = 120
)
$ErrorActionPreference = 'Stop'

$psi = [System.Diagnostics.ProcessStartInfo]::new($Exe)
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$proc = [System.Diagnostics.Process]::Start($psi)

function Send-Rpc {
    param([object]$Id, [string]$Method, $Params)
    $msg = @{ jsonrpc = "2.0"; method = $Method }
    if ($null -ne $Id)     { $msg.id = $Id }
    if ($null -ne $Params) { $msg.params = $Params }
    $proc.StandardInput.WriteLine(($msg | ConvertTo-Json -Depth 10 -Compress))
    $proc.StandardInput.Flush()
    if ($null -eq $Id) { return $null }

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $line = $proc.StandardOutput.ReadLine()
        if (-not $line) { continue }
        try { $o = $line | ConvertFrom-Json } catch { continue }
        if ($o.id -eq $Id) { return $o }
    }
    throw "timeout waiting for $Method"
}

try {
    Send-Rpc -Id 1 -Method "initialize" -Params @{
        protocolVersion = "2024-11-05"; capabilities = @{}
        clientInfo = @{ name = "drive.ps1"; version = "1" } } | Out-Null
    Send-Rpc -Id $null -Method "notifications/initialized" -Params @{} | Out-Null

    $resp = Send-Rpc -Id 2 -Method "tools/call" -Params @{ name = $Tool; arguments = $Arguments }
    if ($resp.error) { Write-Host "ERROR: $($resp.error.message)" }
    else { ($resp.result.content | Where-Object { $_.type -eq 'text' }).text | Write-Host }
}
finally {
    if (-not $proc.HasExited) { $proc.Kill() }
}
```

- [ ] **Step 7: Build and verify manually**

Run: `dotnet build "DialogEditor.slnx" -c Debug`
Expected: Build succeeded, 0 errors.

Run: `pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Tool session_status`
Expected: `no session`

Run: `pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Tool launch_app -Arguments @{ repoRoot = (Get-Location).Path }`
Expected: `launched: 'Pillars Dialog Editor'` — the app window appears.

Verify teardown restored state. Run:

```powershell
Get-Process -Name DialogEditor.Avalonia -ErrorAction SilentlyContinue
Test-Path "$env:TEMP\PillarsDialogEditor.settings.backup.json"
```

Expected: no process (drive.ps1 kills the server, whose ProcessExit handler restores), and `False` for the leftover backup.

- [ ] **Step 8: Commit**

```bash
git add tools/DialogEditor.UiaMcp DialogEditor.slnx
git commit -m "feat(uiamcp): add MCP server host, session tools and live UIA tree"
```

---

### Task 9: Inspection tools — `read_tree`, `find`, `menu`

**Files:**
- Create: `tools/DialogEditor.UiaMcp/Tools/InspectionTools.cs`
- Modify: `tools/DialogEditor.UiaMcp/EditorSession.cs` (expose the shared `RefTable`)

**Interfaces:**
- Consumes: `Resolver`, `RefTable`, `AddressabilityWarnings`, `Selector` (Tasks 5–7), `EditorSession`, `UiaTree` (Task 8).
- Produces: MCP tools `read_tree`, `find`, `menu`, `window_title`, `read_status_bar`.

- [ ] **Step 1: Expose a RefTable on the session**

In `tools/DialogEditor.UiaMcp/EditorSession.cs`, add to the class body:

```csharp
    /// <summary>Shared across tool calls so refs minted by read_tree resolve in later calls.</summary>
    public RefTable Refs { get; } = new();
```

- [ ] **Step 2: Write the inspection tools**

`tools/DialogEditor.UiaMcp/Tools/InspectionTools.cs`:

```csharp
using System.ComponentModel;
using System.Text;
using DialogEditor.UiaMcp.Core;
using ModelContextProtocol.Server;

namespace DialogEditor.UiaMcp.Tools;

[McpServerToolType]
internal sealed class InspectionTools(EditorSession session)
{
    [McpServerTool, Description(
        "Dump the app's UI Automation tree with refs and addressability warnings. " +
        "Scope with withinPane to keep the output small.")]
    public string ReadTree(
        [Description("Pane name or automation id, e.g. 'Node Details' or 'RightPane'")] string? withinPane = null,
        [Description("'interactive' (focusable or pattern-bearing) or 'all'")] string filter = "interactive")
    {
        var resolver = new Resolver(session.Tree());
        var all = resolver.Flatten(withinPane);

        var shown = filter == "all"
            ? all
            : all.Where(e => e.IsFocusable || e.Patterns.Count > 0 || e.ControlType is "Pane" or "MenuItem").ToList();

        var refs = session.Refs.Mint(shown);

        var sb = new StringBuilder();
        sb.AppendLine($"generation={session.Refs.Generation}  elements={shown.Count} (of {all.Count} in scope)");
        for (var i = 0; i < shown.Count; i++)
        {
            var e = shown[i];
            sb.AppendLine($"{refs[i]} [{e.ControlType}] name='{e.Name}' id='{e.AutomationId}' " +
                          $"enabled={e.IsEnabled} offscreen={e.IsOffscreen} patterns=[{string.Join(",", e.Patterns)}]");
        }

        var warnings = AddressabilityWarnings.Inspect(all);
        if (warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("ADDRESSABILITY WARNINGS (see issue #15):");
            foreach (var w in warnings) sb.AppendLine($"  [{w.Kind}] {w.Detail}");
        }
        return sb.ToString();
    }

    [McpServerTool, Description(
        "The window title, which carries [ProjectName] and a bullet dirty marker.")]
    public string WindowTitle() => session.Status();

    [McpServerTool, Description("Read the status bar's live-region text.")]
    public string ReadStatusBar()
    {
        var all = new Resolver(session.Tree()).Flatten();
        var status = all.FirstOrDefault(e => e.AutomationId == "StatusLiveRegion");
        return status is null
            ? "Error(NotFound): no element with automationId 'StatusLiveRegion'."
            : status.Name;
    }

    [McpServerTool, Description("Find elements whose name, automation id or control type contains the query.")]
    public string Find([Description("Case-insensitive substring")] string query)
    {
        var all = new Resolver(session.Tree()).Flatten();
        var hits = all.Where(e =>
            e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            e.AutomationId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            e.ControlType.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        if (hits.Count == 0) return $"No element matched '{query}'.";

        var refs = session.Refs.Mint(hits);
        var sb = new StringBuilder($"generation={session.Refs.Generation}  matches={hits.Count}\n");
        for (var i = 0; i < hits.Count; i++)
        {
            var e = hits[i];
            sb.AppendLine($"{refs[i]} [{e.ControlType}] name='{e.Name}' id='{e.AutomationId}' enabled={e.IsEnabled}");
        }
        return sb.ToString();
    }

    [McpServerTool, Description(
        "List the app's menu items with enabled state. Omit path for the top-level bar. " +
        "Always scoped to the app's own menu, never the OS window menu.")]
    public string Menu([Description("Menu path, e.g. ['File']")] string[]? path = null)
    {
        var tree = session.Tree();
        var resolver = new Resolver(tree);

        // The app's Menu is anonymous (audit finding 5) — ClassName is the only handle,
        // and scoping here is what stops the OS 'System' menu being treated as a peer
        // of File/Edit/View/Test/Help.
        var appMenu = resolver.Flatten().FirstOrDefault(e => e.ClassName == "Menu" && e.ControlType == "Menu");
        if (appMenu is null)
            return "Error(NotFound): the app's menu (ClassName='Menu') was not found.";

        var current = appMenu;
        foreach (var segment in path ?? Array.Empty<string>())
        {
            var next = tree.ChildrenOf(current.Id)
                .FirstOrDefault(c => string.Equals(c.Name, segment, StringComparison.Ordinal));
            if (next is null)
            {
                var available = string.Join(", ",
                    tree.ChildrenOf(current.Id).Where(c => c.Name.Length > 0).Select(c => $"'{c.Name}'"));
                return $"Error(NotFound): no menu item '{segment}' under '{current.Name}'. " +
                       $"Available: {available}. Note menu labels use the ellipsis character '…', not three dots.";
            }
            current = next;
        }

        var sb = new StringBuilder();
        foreach (var item in tree.ChildrenOf(current.Id).Where(c => c.ControlType == "MenuItem"))
            sb.AppendLine($"{item.Name} | enabled={item.IsEnabled} | id='{item.AutomationId}'");

        return sb.Length == 0
            ? $"'{current.Name}' has no child menu items (open it first if it is a popup menu)."
            : sb.ToString();
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build "DialogEditor.slnx" -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Verify manually against the live app**

Run:

```powershell
pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Tool launch_app -Arguments @{ repoRoot = (Get-Location).Path }
```

Then, in a fresh invocation for each, confirm:

- `-Tool read_tree -Arguments @{ withinPane = "Conversations" }` — lists refs and prints an `UnnamedFocusable` warning naming 37 `TreeItem` elements plus a `NoPatterns` warning. Those two warnings are the expected output today; they are audit finding 1 and must appear until issue #15 fixes it.
- `-Tool find -Arguments @{ query = "Viewbox" }` — returns the six `Avalonia.Controls.Viewbox` buttons.
- `-Tool menu` — lists exactly `File, Edit, View, Test, Help` and **not** `System`.
- `-Tool menu -Arguments @{ path = @("File") }` — lists File's items with `enabled=` reflecting whether a project is open.
- `-Tool read_status_bar` — returns the current status text, e.g. `Opened project '…' (0 patches)`.
- `-Tool window_title` — reports the held session's pid and window title.

- [ ] **Step 5: Run the whole test suite**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`
Expected: PASS, no regressions.

- [ ] **Step 6: Commit**

```bash
git add tools/DialogEditor.UiaMcp/Tools/InspectionTools.cs tools/DialogEditor.UiaMcp/EditorSession.cs
git commit -m "feat(uiamcp): add read_tree, find and menu inspection tools"
```

---

### Task 10: Build and test tools

**Files:**
- Create: `tools/DialogEditor.UiaMcp/Tools/BuildTools.cs`
- Create: `tools/DialogEditor.UiaMcp/ProcessRunner.cs`

**Interfaces:**
- Consumes: `BuildOutputParser`, `TestOutputParser` (Tasks 2–3).
- Produces: MCP tools `build`, `run_tests`.

- [ ] **Step 1: Write the process runner**

`tools/DialogEditor.UiaMcp/ProcessRunner.cs`:

```csharp
using System.Diagnostics;
using System.Text;

namespace DialogEditor.UiaMcp;

internal static class ProcessRunner
{
    /// <summary>Runs a command, capturing stdout+stderr together. Returns combined output.</summary>
    public static string Run(string fileName, string arguments, string workingDirectory, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}.");

        var sb = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        if (!p.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { p.Kill(entireProcessTree: true); }
            catch (Exception ex) { Console.Error.WriteLine($"Failed to kill timed-out process: {ex}"); }
            sb.AppendLine($"[timed out after {timeout.TotalSeconds:n0}s]");
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 2: Write the build/test tools**

`tools/DialogEditor.UiaMcp/Tools/BuildTools.cs`:

```csharp
using System.ComponentModel;
using System.Text;
using DialogEditor.UiaMcp.Core;
using ModelContextProtocol.Server;

namespace DialogEditor.UiaMcp.Tools;

[McpServerToolType]
internal sealed class BuildTools
{
    [McpServerTool, Description("Build the solution and return a compact result with diagnostics.")]
    public string Build(
        [Description("Absolute path to the repository root")] string repoRoot,
        [Description("Debug or Release")] string configuration = "Debug",
        [Description("Project or solution to build; defaults to DialogEditor.slnx")] string? project = null)
    {
        var target = project ?? "DialogEditor.slnx";
        var output = ProcessRunner.Run("dotnet",
            $"build \"{target}\" -c {configuration} --nologo",
            repoRoot, TimeSpan.FromMinutes(10));

        var r = BuildOutputParser.Parse(output);

        var sb = new StringBuilder();
        sb.AppendLine(r.Succeeded
            ? $"Build succeeded. {r.ErrorCount} error(s), {r.WarningCount} warning(s)."
            : $"Build FAILED. {r.ErrorCount} error(s), {r.WarningCount} warning(s).");
        foreach (var d in r.Diagnostics)
            sb.AppendLine($"  {d.Severity} {d.Code} {d.File}:{d.Line}:{d.Column} — {d.Message}");
        if (r.Truncated) sb.AppendLine("  … more diagnostics omitted.");
        return sb.ToString();
    }

    [McpServerTool, Description("Run the test suite and return counts plus named failures.")]
    public string RunTests(
        [Description("Absolute path to the repository root")] string repoRoot,
        [Description("xunit filter expression, e.g. FullyQualifiedName~ResolverTests")] string? filter = null,
        [Description("Test project; defaults to DialogEditor.Tests")] string? project = null)
    {
        var target = project ?? "DialogEditor.Tests/DialogEditor.Tests.csproj";
        var args = $"test \"{target}\" --nologo";
        if (!string.IsNullOrWhiteSpace(filter)) args += $" --filter \"{filter}\"";

        var output = ProcessRunner.Run("dotnet", args, repoRoot, TimeSpan.FromMinutes(20));
        var r = TestOutputParser.Parse(output);

        var sb = new StringBuilder();
        sb.AppendLine(r.Succeeded
            ? $"Tests passed. {r.Passed} passed, {r.Skipped} skipped, {r.Total} total."
            : $"Tests FAILED. {r.Failed} failed, {r.Passed} passed, {r.Skipped} skipped, {r.Total} total.");
        foreach (var f in r.Failures)
            sb.AppendLine($"  FAILED {f.TestName}\n    {f.Message.Replace("\n", "\n    ")}");
        if (r.Truncated) sb.AppendLine("  … more failures omitted.");
        return sb.ToString();
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build "DialogEditor.slnx" -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Verify manually**

Run:

```powershell
pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Tool build -Arguments @{ repoRoot = (Get-Location).Path }
```

Expected: `Build succeeded.` with 0 errors. Warning count depends on what recompiled — a clean build reports 8 pre-existing warnings, an incremental one reports 0. Only the error count is a pass/fail signal here.

Run:

```powershell
pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Tool run_tests -Arguments @{ repoRoot = (Get-Location).Path; filter = "FullyQualifiedName~ResolverTests" }
```

Expected: `Tests passed. 11 passed, 0 skipped, 11 total.`

- [ ] **Step 5: Commit**

```bash
git add tools/DialogEditor.UiaMcp/ProcessRunner.cs tools/DialogEditor.UiaMcp/Tools/BuildTools.cs
git commit -m "feat(uiamcp): add build and run_tests tools with parsed output"
```

---

### Task 11: Register the server and document it

**Files:**
- Create: `tools/DialogEditor.UiaMcp/README.md`
- Modify: `.claude/skills/running-the-app/SKILL.md`

**Interfaces:**
- Consumes: everything above.
- Produces: no code.

- [ ] **Step 1: Write the README**

`tools/DialogEditor.UiaMcp/README.md`:

```markdown
# DialogEditor.UiaMcp

An MCP server that drives the Dialog Editor's GUI from outside via Windows UI
Automation, and runs builds and tests.

Design: `docs/superpowers/specs/2026-09-03-uia-mcp-server-design.md`
Audit that shaped it: `docs/2026-09-03-uia-addressability-audit.md`

## Security boundary

This server contains **no in-app component**. It drives the editor purely through
OS-level accessibility APIs and synthetic input on the local desktop, exactly as
`tools/ui-automation/DriveApp.ps1` does. The app has no remote-control endpoint,
network test hook, or automation backdoor, and none may be added on this server's
account — see the **UI Automation Support** rule in `CLAUDE.md`.

## Registering with Claude Code

    claude mcp add dialog-editor-uia -- \
      "<repo>/tools/DialogEditor.UiaMcp/bin/Debug/net8.0-windows/DialogEditor.UiaMcp.exe"

Requires a Debug build and a real interactive desktop; GUI tools cannot run headless.

## Tools

| Tool | Purpose |
|---|---|
| `launch_app` | Launch the editor, back up settings, hold the session |
| `session_status` | Report the held session |
| `kill_app` | Kill the app and restore settings |
| `read_tree` | Tree with refs plus addressability warnings |
| `find` | Substring search across name, automation id, control type |
| `menu` | List menu items, scoped to the app's menu |
| `build` | Build with parsed diagnostics |
| `run_tests` | Run tests with parsed failures |

## Why warnings, not workarounds

`read_tree` reports what it cannot address. That is deliberate: an element unreachable
by name is a defect a screen-reader user hits too, so the harness must keep the signal
visible rather than route around it. Every warning maps to a finding in the audit and
is tracked by [issue #15](https://github.com/kjmikkel/PillarsDialogEditor/issues/15).

## Manual smoke test

    pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Tool menu
```

- [ ] **Step 2: Point the skill at the server**

In `.claude/skills/running-the-app/SKILL.md`, add immediately after the opening
paragraph:

```markdown
**Prefer the MCP server when it is registered.** `tools/DialogEditor.UiaMcp` exposes
`launch_app`, `read_tree`, `find`, `menu`, `build` and `run_tests` as tools, with the
gotchas below already encoded — including erroring on ambiguous names rather than
silently clicking the first match. Fall back to the PowerShell path below when the
server is not registered, or for anything it does not yet cover (clicks, typing,
screenshots — not yet built).
```

- [ ] **Step 3: Final full verification**

Run: `dotnet build "DialogEditor.slnx" -c Debug`
Expected: Build succeeded, 0 errors.

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`
Expected: PASS — all pre-existing tests plus the 44 added by this plan.

- [ ] **Step 4: Commit**

```bash
git add tools/DialogEditor.UiaMcp/README.md .claude/skills/running-the-app/SKILL.md
git commit -m "docs(uiamcp): document the server and point the running-the-app skill at it"
```

---

## Out of scope — follow-up plan

Deliberately not built here, per the spec's sequencing:

- **Phase 3 — actions:** `invoke`, `invoke_menu`, `send_keys`, `set_value`, `focus`,
  `screenshot`, with pattern-preferred execution and warned synthetic-click fallback,
  plus scroll-into-view handling for the offscreen conversation rows.
- **Phase 4 — fallback removal:** as each issue #15 phase lands, delete the
  corresponding fallback and promote the `read_tree` warnings into an enforcement test.

The coupling worth pulling forward into #15 is finding 4 — stable `AutomationId`s on all
51 menu items — which would decouple `menu` and the future `invoke_menu` from localised
label text.
