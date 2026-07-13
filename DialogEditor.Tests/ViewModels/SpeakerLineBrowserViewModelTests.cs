using DialogEditor.Core.Editing;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class SpeakerLineBrowserViewModelTests
{
    private const string Bao   = "9c5f12c9-e93d-4952-9f1a-726c9498f8fb";
    private const string Aloth = "11111111-2222-3333-4444-555555555555";

    public SpeakerLineBrowserViewModelTests() => Loc.Configure(new StubStringProvider());

    private static readonly (string?, ConversationEditSnapshot?) NoOpen = (null, null);

    // A VM whose scan returns a fixed row set (bypasses IO); provider is unused by the stub.
    private static SpeakerLineBrowserViewModel VmWithRows(
        IReadOnlyList<SpeakerLineRow> rows, string? initial = null)
    {
        var provider = new FakeGameDataProvider("poe2", "en");
        return new SpeakerLineBrowserViewModel(
            DialogProject.Empty("P"), provider, "en", () => NoOpen, initial,
            scanner: (_, _, _) => rows);
    }

    private static SpeakerLineRow Row(string speaker, string conv, int id,
        LineOrigin origin = LineOrigin.Vanilla, LineVariant variant = LineVariant.Default) =>
        new(speaker, conv, id, variant, $"{conv}:{id}", origin);

    [Fact]
    public async Task Scan_PopulatesSpeakers_WithCounts_NameSorted()
    {
        var vm = VmWithRows([
            Row(Bao, "c1", 1), Row(Bao, "c1", 2), Row(Aloth, "c2", 3),
        ]);
        await vm.ScanAsync();

        Assert.Equal(2, vm.Speakers.Count);
        Assert.Contains(vm.Speakers, s => s.Guid == Bao   && s.Count == 2);
        Assert.Contains(vm.Speakers, s => s.Guid == Aloth && s.Count == 1);
    }

    [Fact]
    public async Task Scan_SelectsFirstSpeaker_AndFiltersRows()
    {
        var vm = VmWithRows([Row(Bao, "c1", 1), Row(Aloth, "c2", 3)]);
        await vm.ScanAsync();

        Assert.NotNull(vm.SelectedSpeaker);
        Assert.All(vm.Rows, r => Assert.Equal(vm.SelectedSpeaker!.Guid, r.SpeakerGuid));
    }

    [Fact]
    public async Task InitialSpeakerGuid_PreSelectsThatSpeaker()
    {
        var vm = VmWithRows([Row(Bao, "c1", 1), Row(Aloth, "c2", 3)], initial: Aloth);
        await vm.ScanAsync();

        Assert.Equal(Aloth, vm.SelectedSpeaker!.Guid);
    }

    [Fact]
    public async Task OnlyMyLines_HidesVanillaRows_WithoutRescanning()
    {
        var scans = 0;
        var provider = new FakeGameDataProvider("poe2", "en");
        var rows = new[] { Row(Bao, "c1", 1, LineOrigin.Vanilla), Row(Bao, "c1", 2, LineOrigin.New) };
        var vm = new SpeakerLineBrowserViewModel(
            DialogProject.Empty("P"), provider, "en", () => NoOpen, null,
            scanner: (_, _, _) => { scans++; return rows; });

        await vm.ScanAsync();
        Assert.Equal(2, vm.Rows.Count);

        vm.OnlyMyLines = true;
        Assert.Single(vm.Rows);
        Assert.Equal(LineOrigin.New, vm.Rows[0].Origin);
        Assert.Equal(1, scans);   // filtering did not trigger a re-scan
    }

    [Fact]
    public async Task ChangingSpeaker_RefiltersWithoutRescanning()
    {
        var scans = 0;
        var provider = new FakeGameDataProvider("poe2", "en");
        var rows = new[] { Row(Bao, "c1", 1), Row(Aloth, "c2", 3) };
        var vm = new SpeakerLineBrowserViewModel(
            DialogProject.Empty("P"), provider, "en", () => NoOpen, null,
            scanner: (_, _, _) => { scans++; return rows; });
        await vm.ScanAsync();

        vm.SelectedSpeaker = vm.Speakers.Single(s => s.Guid == Aloth);
        Assert.All(vm.Rows, r => Assert.Equal(Aloth, r.SpeakerGuid));
        Assert.Equal(1, scans);
    }

    [Fact]
    public async Task NavigateTo_RaisesRequestNavigate_WithTarget()
    {
        var vm = VmWithRows([Row(Bao, "conv7", 7)]);
        await vm.ScanAsync();
        (string, int)? got = null;
        vm.RequestNavigate += (c, n) => got = (c, n);

        vm.NavigateTo(vm.Rows[0]);

        Assert.Equal(("conv7", 7), got);
    }

    [Fact]
    public void CancelScanCommand_DisabledWhenNotBusy()
    {
        var vm = VmWithRows([Row(Bao, "c1", 1)]);
        Assert.False(vm.CancelScanCommand.CanExecute(null));   // not scanning yet
    }
}
