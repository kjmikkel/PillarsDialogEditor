using System.Security.Cryptography;
using DialogEditor.Patch.Install;
using DialogEditor.PatchCli;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.CanonicalMod;

/// The three ways a canonical mod reaches a game install (issue 138, spec §3), each through
/// its real entry point, plus a fingerprint of the game files to compare the results. Owns
/// the temp installs it creates and deletes them on Dispose.
public sealed class DeliveryPaths : IDisposable
{
    private readonly List<string> _roots = [];

    /// A fresh copy of the canonical install. The path contains a space on purpose.
    public string FreshInstall(string game)
    {
        var root = Path.Combine(Path.GetTempPath(), $"canonical install {Guid.NewGuid():N}");
        CanonicalFixture.CopyTo(game, root);
        _roots.Add(root);
        return root;
    }

    /// dialog-patcher, in-process: argument parsing and pack extraction included.
    public static void ViaPatcher(string gameRoot, string pack)
    {
        var err  = new StringWriter();
        var code = PatcherCommand.Run([gameRoot, pack], new StringWriter(), err);
        Assert.True(code == 0, $"dialog-patcher exited {code}: {err}");
    }

    /// The Patch Manager: Add mods… answered with the pack, then Apply patches.
    public static async Task ViaPatchManagerAsync(string gameRoot, string pack)
    {
        Loc.Configure(new StubStringProvider());
        var vm = new PatchManagerViewModel(new StubFolderPicker(), new StubFilePicker(multiResult: [pack])) { GameFolder = gameRoot };
        await vm.AddEntriesCommand.ExecuteAsync(null);
        Assert.All(vm.Entries, e => Assert.True(string.IsNullOrEmpty(e.LoadError), e.LoadError));
        Assert.Single(vm.Entries);
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.True(PatcherBackupInfo.TryOpen(gameRoot) is not null, $"Patch Manager did not install: {vm.StatusText}");
    }

    /// Test Patch (F5) from the editor, with the project open over the install.
    public async Task ViaTestPatchAsync(string gameRoot, string projectPath)
    {
        using var session = new CanonicalModSession();
        await session.LoadExistingAsync(gameRoot, projectPath);
        await session.Vm.TestPatchCommand.ExecuteAsync(null);
    }

    /// The Patch Manager's Remove all mods, with its confirmation answered yes.
    public static async Task RemoveAllModsAsync(string gameRoot)
    {
        Loc.Configure(new StubStringProvider());
        var vm = new PatchManagerViewModel(new StubFolderPicker(), new StubFilePicker())
        {
            GameFolder           = gameRoot,
            ConfirmRemoveAllMods = () => Task.FromResult(true),
        };
        await vm.RemoveAllModsCommand.ExecuteAsync(null);
    }

    /// Test Patch (F5), then Restore Conversation (F6) in the same editor session.
    public async Task ViaTestPatchAndRestoreAsync(string gameRoot, string projectPath)
    {
        using var session = new CanonicalModSession();
        await session.LoadExistingAsync(gameRoot, projectPath);
        await session.Vm.TestPatchCommand.ExecuteAsync(null);
        // Test Patch reports a refusal only in the status bar, and a restore with nothing to
        // undo leaves the fixture as it was. Prove F5 really wrote before checking F6.
        Assert.True(File.Exists(Path.Combine(gameRoot, "PillarsOfEternityII_Data", "exported", "design", "conversations",
                                             Poe2Scenario.NewConversation + ".conversationbundle")),
                    $"Test Patch wrote nothing: {session.Vm.StatusText}");
        await session.Vm.RestoreConversationCommand.ExecuteAsync(null);
    }

    /// Relative path → SHA-256 of every file the game reads (under the data folder). The
    /// patcher's and the editor's own backup folders live outside it.
    public static SortedDictionary<string, string> GameFiles(string gameRoot)
    {
        var data   = Directory.EnumerateDirectories(gameRoot, "PillarsOfEternity*_Data").Single();
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories))
            result[Path.GetRelativePath(gameRoot, file).Replace('\\', '/')] =
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        return result;
    }

    /// Names each path that only one side has or whose content differs; null when equal.
    public static string? Describe(SortedDictionary<string, string> expected, SortedDictionary<string, string> actual)
    {
        var lines = expected.Keys.Union(actual.Keys).Order(StringComparer.Ordinal)
            .Select(k => !actual.ContainsKey(k)   ? $"only in expected: {k}"
                       : !expected.ContainsKey(k) ? $"only in actual:   {k}"
                       : expected[k] != actual[k] ? $"differs:          {k}"
                       : null)
            .OfType<string>()
            .ToList();
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    public void Dispose()
    {
        foreach (var root in _roots)
            try { Directory.Delete(root, true); } catch (Exception) { /* best-effort */ }
    }
}
