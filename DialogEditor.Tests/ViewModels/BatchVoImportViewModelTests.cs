using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class BatchVoImportViewModelTests
{
    public BatchVoImportViewModelTests() => Loc.Configure(new StubStringProvider());

    private static BatchVoRowViewModel MakeRow(
        VoPresence status = VoPresence.Missing,
        string? primarySrc = null) =>
        new("conv", 1, "hello", status, "C:/dest/a.wem", "C:/dest/a_fem.wem")
            { PrimarySourcePath = primarySrc };

    // ── ShowOnlyMissing ──────────────────────────────────────────────────

    [Fact]
    public void ShowOnlyMissing_True_ExcludesFoundRows()
    {
        var found   = MakeRow(VoPresence.Found);
        var missing = MakeRow(VoPresence.Missing);
        var vm = new BatchVoImportViewModel([found, missing], new StubImporter());
        vm.ShowOnlyMissing = true;

        Assert.DoesNotContain(found,   vm.VisibleRows);
        Assert.Contains(missing, vm.VisibleRows);
    }

    [Fact]
    public void ShowOnlyMissing_False_ShowsAllRows()
    {
        var found   = MakeRow(VoPresence.Found);
        var missing = MakeRow(VoPresence.Missing);
        var vm = new BatchVoImportViewModel([found, missing], new StubImporter());
        vm.ShowOnlyMissing = false;

        Assert.Contains(found,   vm.VisibleRows);
        Assert.Contains(missing, vm.VisibleRows);
    }

    // ── ImportCommand ────────────────────────────────────────────────────

    [Fact]
    public async Task Import_SetsRowStatusDone_WhenImporterSucceeds()
    {
        var row  = MakeRow(primarySrc: "C:/src/a.wem");
        var stub = new StubImporter(success: true);
        var vm   = new BatchVoImportViewModel([row], stub);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(BatchRowStatus.Done, row.RowStatus);
        Assert.Equal(1, stub.CallCount);
    }

    [Fact]
    public async Task Import_SetsRowStatusError_WhenImporterFails()
    {
        var row  = MakeRow(primarySrc: "C:/src/a.wem");
        var stub = new StubImporter(success: false);
        var vm   = new BatchVoImportViewModel([row], stub);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(BatchRowStatus.Error, row.RowStatus);
        Assert.NotNull(row.ErrorMessage);
    }

    [Fact]
    public async Task Import_SkipsRowsWithoutSourcePath()
    {
        var noSource = MakeRow();                          // PrimarySourcePath = null
        var withSrc  = MakeRow(primarySrc: "C:/src/b.wem");
        var stub = new StubImporter(success: true);
        var vm   = new BatchVoImportViewModel([noSource, withSrc], stub);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(BatchRowStatus.Pending, noSource.RowStatus);
        Assert.Equal(BatchRowStatus.Done,    withSrc.RowStatus);
        Assert.Equal(1, stub.CallCount);
    }

    [Fact]
    public async Task Import_StopsOnCancellation_RemainingRowsStayPending()
    {
        var row1 = MakeRow(primarySrc: "C:/src/a.wem");
        var row2 = MakeRow(primarySrc: "C:/src/b.wem");
        BatchVoImportViewModel? captured = null;
        var stub = new StubImporter(onCall: () => captured?.Cancel());
        var vm   = new BatchVoImportViewModel([row1, row2], stub);
        captured = vm;

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(BatchRowStatus.Done,    row1.RowStatus);
        Assert.Equal(BatchRowStatus.Pending, row2.RowStatus);
    }

    // ── Stub ─────────────────────────────────────────────────────────────

    private sealed class StubImporter : IVoImporter
    {
        private readonly bool    _success;
        private readonly Action? _onCall;
        public int CallCount { get; private set; }

        public StubImporter(bool success = true, Action? onCall = null)
        {
            _success = success;
            _onCall  = onCall;
        }

        public bool IsWwiseAvailable => false;

        public Task<VoImportResult> ImportAsync(VoImportRequest request, CancellationToken ct)
        {
            CallCount++;
            _onCall?.Invoke();
            return Task.FromResult(new VoImportResult(_success,
                _success ? null : "Stub failure"));
        }
    }
}
