using DialogEditor.Core.Backup;
using DialogEditor.Core.Logging;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Backup;

/// The full backup's on-disk layout (issue 123): conversations plus every installed
/// language's stringtables, described by a backup.json manifest.
public class FullBackupTests : IDisposable
{
    private readonly FakePoe2Game _game = FakePoe2Game.Canonical();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fullbackup_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (Exception) { /* best-effort */ }
        _game.Dispose();
    }

    [Fact]
    public async Task TakeAsync_SnapshotsConversationsAndEveryLanguage()
    {
        await FullBackup.TakeAsync(_game.Provider, _root, default);

        Assert.True(File.Exists(Path.Combine(_root, "conversations", "canonical.conversationbundle")));
        foreach (var lang in new[] { "en", "de" })
            Assert.Equal(File.ReadAllBytes(_game.StPath(lang, "canonical")),
                File.ReadAllBytes(Path.Combine(_root, "stringtables", lang, "canonical.stringtable")));
        Assert.False(Directory.Exists(Path.Combine(_root, "voice-over")));   // filled lazily by Test Patch
    }

    [Fact]
    public async Task TakeAsync_WritesAFormat2Manifest()
    {
        await FullBackup.TakeAsync(_game.Provider, _root, default);

        var manifest = FullBackup.ReadManifest(_root);

        Assert.NotNull(manifest);
        Assert.Equal(2, manifest.Format);
        Assert.Equal("poe2", manifest.GameId);
        Assert.Equal(["de", "en"], manifest.Languages);
    }

    [Fact]
    public void ReadManifest_WithoutBackupJson_IsNull()
    {
        Directory.CreateDirectory(_root);
        Assert.Null(FullBackup.ReadManifest(_root));
    }

    [Fact]
    public void ReadManifest_CorruptBackupJson_IsNullAndLogged()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, FullBackup.ManifestFileName), "{ not json");

        Assert.Null(FullBackup.ReadManifest(_root));

        Assert.Contains("backup.json", File.ReadAllText(AppLog.LogPath));
    }

    [Fact]
    public void Latest_IsTheLexicographicallyLastSubfolder()
    {
        Directory.CreateDirectory(Path.Combine(_root, "2026-01-01T10-00"));
        Directory.CreateDirectory(Path.Combine(_root, "2026-03-01T10-00"));
        Directory.CreateDirectory(Path.Combine(_root, "2026-02-01T10-00"));

        Assert.Equal(Path.Combine(_root, "2026-03-01T10-00"), FullBackup.Latest(_root));
    }

    [Fact]
    public void Latest_NoSubfoldersOrMissingFolder_IsNull()
    {
        Assert.Null(FullBackup.Latest(_root));
        Directory.CreateDirectory(_root);
        Assert.Null(FullBackup.Latest(_root));
    }

    // ── PlanRestore ──────────────────────────────────────────────────────

    private void WriteLegacyBackup(string fromLanguage)
    {
        var conversations = Path.Combine(_root, "conversations");
        Directory.CreateDirectory(conversations);
        File.Copy(_game.ConvPath("canonical"), Path.Combine(conversations, "canonical.conversationbundle"));
        var flat = Path.Combine(_root, "stringtables");
        Directory.CreateDirectory(flat);
        File.Copy(_game.StPath(fromLanguage, "canonical"), Path.Combine(flat, "canonical.stringtable"));
    }

    [Fact]
    public async Task PlanRestore_Format2_PairsConversationsAndEachLanguage()
    {
        var provider = _game.Provider;
        await FullBackup.TakeAsync(provider, _root, default);

        var plan = FullBackup.PlanRestore(provider, _root, _game.VoDir);

        Assert.False(plan.TextLanguageUnknown);
        Assert.Equal(
        [
            new RestorePair(Path.Combine(_root, "conversations"), provider.GetBackupRoots().ConversationsRoot),
            new RestorePair(Path.Combine(_root, "stringtables", "de"), provider.GetStringTablesRoot("de")),
            new RestorePair(Path.Combine(_root, "stringtables", "en"), provider.GetStringTablesRoot("en")),
        ], plan.Pairs);
    }

    [Fact]
    public async Task PlanRestore_Format2_AddsVoiceOverOnceItHasContent()
    {
        await FullBackup.TakeAsync(_game.Provider, _root, default);
        Directory.CreateDirectory(Path.Combine(_root, "voice-over"));

        var plan = FullBackup.PlanRestore(_game.Provider, _root, _game.VoDir);

        Assert.Contains(new RestorePair(Path.Combine(_root, "voice-over"), _game.VoDir), plan.Pairs);
    }

    [Fact]
    public async Task PlanRestore_Format2_LanguageUninstalledSince_OmitsItsPair()
    {
        await FullBackup.TakeAsync(_game.Provider, _root, default);
        Directory.Delete(_game.StDir("de"), recursive: true);
        var provider = _game.Provider;

        var plan = FullBackup.PlanRestore(provider, _root, _game.VoDir);

        Assert.DoesNotContain(plan.Pairs, p => p.Snapshot.EndsWith(Path.Combine("stringtables", "de")));
        Assert.Contains(plan.Pairs, p => p.Snapshot.EndsWith(Path.Combine("stringtables", "en")));
    }

    [Fact]
    public void PlanRestore_Format1_PairsTheFlatFolderWithTheInferredLanguage_WhateverIsSelected()
    {
        WriteLegacyBackup("de");
        var provider = _game.Provider;
        provider.Language = "en";

        Assert.Equal("de", FullBackup.InferLegacyLanguage(provider, Path.Combine(_root, "stringtables")));
        var plan = FullBackup.PlanRestore(provider, _root, _game.VoDir);

        Assert.False(plan.TextLanguageUnknown);
        Assert.Contains(new RestorePair(Path.Combine(_root, "stringtables"), provider.GetStringTablesRoot("de")), plan.Pairs);
    }

    [Fact]
    public void PlanRestore_Format1_UnmatchedText_RestoresConversationsOnlyAndSaysSo()
    {
        WriteLegacyBackup("de");
        File.WriteAllText(Path.Combine(_root, "stringtables", "canonical.stringtable"), "matches no language");
        var provider = _game.Provider;

        Assert.Null(FullBackup.InferLegacyLanguage(provider, Path.Combine(_root, "stringtables")));
        var plan = FullBackup.PlanRestore(provider, _root, _game.VoDir);

        Assert.True(plan.TextLanguageUnknown);
        var pair = Assert.Single(plan.Pairs);
        Assert.Equal(Path.Combine(_root, "conversations"), pair.Snapshot);
    }

    [Fact]
    public void InferLegacyLanguage_TwoLanguagesMatchEqually_IsNull()
    {
        WriteLegacyBackup("en");
        File.Copy(_game.StPath("en", "canonical"), _game.StPath("de", "canonical"), overwrite: true);

        Assert.Null(FullBackup.InferLegacyLanguage(_game.Provider, Path.Combine(_root, "stringtables")));
    }
}
