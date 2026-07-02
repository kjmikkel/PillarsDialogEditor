using System.Collections.Generic;
using System.IO;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class VoValidationViewModelTests : IDisposable
{
    private readonly string _gameRoot;
    private readonly string _voRoot;

    public VoValidationViewModelTests()
    {
        Loc.Configure(new StubStringProvider());
        _gameRoot = Path.Combine(Path.GetTempPath(), $"VoVmTest_{Guid.NewGuid():N}");
        _voRoot   = Path.Combine(_gameRoot,
            "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
        Directory.CreateDirectory(_voRoot);

        ChatterPrefixService.Register(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "eder" }
        });
    }

    public void Dispose()
    {
        ChatterPrefixService.Clear();
        if (Directory.Exists(_gameRoot))
            Directory.Delete(_gameRoot, recursive: true);
    }

    private static NodeEditSnapshot MakeNode(
        int id,
        string speakerGuid = "9c5f12c9-e93d-4952-9f1a-726c9498f8fb",
        bool hasVO = false,
        string externalVO = "",
        string defaultText = "Test text") =>
        new(id, false, SpeakerCategory.Npc, speakerGuid, "",
            defaultText, "", "Conversation", "None", "", "", externalVO, hasVO, false, [], [], []);

    [Fact]
    public async Task RunAsync_NoVoNodes_ResultsEmpty_SummaryShowsZero()
    {
        var nodes = new[] { MakeNode(1, hasVO: false) };
        var vm = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe2");

        await vm.RunAsync();

        Assert.Empty(vm.Results);
        // StubStringProvider echoes keys verbatim; Loc.Format returns the key unchanged.
        // We assert that a summary key (not the "running" key) is set.
        Assert.Contains("VoValidation_Summary", vm.SummaryText);
    }

    [Fact]
    public async Task RunAsync_HasVO_FileMissing_AddsIssue()
    {
        var nodes = new[] { MakeNode(1, hasVO: true) };
        var vm = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe2");

        await vm.RunAsync();

        Assert.Single(vm.Results);
        Assert.True(vm.Results[0].IsMissing);
        Assert.Equal(1, vm.Results[0].NodeId);
    }

    [Fact]
    public async Task RunAsync_HasVO_FileExists_NoIssueAdded()
    {
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0001.wem"), "");

        var nodes = new[] { MakeNode(1, hasVO: true) };
        var vm = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe2");

        await vm.RunAsync();

        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task RunAsync_SummaryText_ReflectsCheckedAndMissingCounts()
    {
        // Node 1: file present, node 2: file missing
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0001.wem"), "");

        var nodes = new[] { MakeNode(1, hasVO: true), MakeNode(2, hasVO: true) };
        var vm = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe2");

        await vm.RunAsync();

        // StubStringProvider echoes keys verbatim; assert that the summary key is set and
        // that the observable collection reflects the actual checked/missing counts.
        Assert.Contains("VoValidation_Summary", vm.SummaryText);
        Assert.Single(vm.Results);        // 1 missing
        Assert.Equal(2, vm.Results[0].NodeId); // node 2 is missing; only 1 issue
    }

    [Fact]
    public async Task RunAsync_TextPreviewTruncatedAt60Chars()
    {
        var long70 = new string('x', 70);
        var nodes  = new[] { MakeNode(1, hasVO: true, defaultText: long70) };
        var vm     = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe2");

        await vm.RunAsync();

        Assert.Single(vm.Results);
        Assert.True(vm.Results[0].TextPreview.Length <= 63); // 60 chars + "…"
        Assert.EndsWith("…", vm.Results[0].TextPreview);
    }

    [Fact]
    public async Task RunAsync_IsRunningFalseAfterCompletion()
    {
        var nodes = new[] { MakeNode(1, hasVO: true) };
        var vm = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe2");

        await vm.RunAsync();

        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task RunAgainCommand_ClearsResultsAndReruns()
    {
        var nodes = new[] { MakeNode(1, hasVO: true) };
        var vm = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe2");

        await vm.RunAsync(); // first run — 1 missing
        Assert.Single(vm.Results);

        // Plant the file so second run finds it
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0001.wem"), "");

        await vm.RunAsync(); // second run
        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task RunAsync_FileOnlyInProjectVoFolder_NotReportedMissing()
    {
        // Regression (B-006): after F6 the synced game copy is gone, but the VO is
        // still staged in the project's _vo/ folder and will be re-synced on F5 —
        // validation must treat it as present, matching the detail pane's fallback.
        var projectDir = Path.Combine(_gameRoot, "proj");
        Directory.CreateDirectory(Path.Combine(projectDir, "_vo", "eder"));
        File.WriteAllText(Path.Combine(projectDir, "_vo", "eder", "test_conv_0001.wem"), "");
        var projectPath = Path.Combine(projectDir, "test.dialogproject");

        var nodes = new[] { MakeNode(1, hasVO: true) };
        var vm    = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe2", projectPath);

        await vm.RunAsync();

        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task RunAsync_Poe1GameId_ResultsEmpty()
    {
        var nodes = new[] { MakeNode(1, hasVO: true) };
        var vm = new VoValidationViewModel(nodes, "test_conv", _gameRoot, "poe1");

        await vm.RunAsync();

        Assert.Empty(vm.Results);
    }
}
