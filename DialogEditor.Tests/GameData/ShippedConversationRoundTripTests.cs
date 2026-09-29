using System.Text;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;
using DialogEditor.Core.Serialization;
using Xunit.Abstractions;
using static DialogEditor.Tests.Helpers.RoundTripChecks;

namespace DialogEditor.Tests.GameData;

/// <summary>
/// Opt-in fidelity check against a real game install (issue 117): every shipped
/// conversation and stringtable is saved unchanged, in memory, through the same
/// serializers F5, dialog-patcher and the Patch Manager use, and must come back intact.
/// </summary>
/// <remarks>
/// Runs only when DIALOGEDITOR_POE1_DIR / DIALOGEDITOR_POE2_DIR name an install
/// (<see cref="GameInstallFactAttribute"/>); see docs/game-data-tests.md. Nothing is ever
/// written to disk. The checks, per game, with no allow-list:
/// <list type="bullet">
/// <item>the editor's model of every conversation is identical after the save;</item>
/// <item>PoE1: the saved XML is equivalent to the original in the game's own model —
///   both are deserialized with XmlSerializer over OEIFormats.ConversationData, loaded
///   from the install, and re-serialized. Formatting (&lt;X /&gt; vs &lt;X&gt;&lt;/X&gt;)
///   and elements the game ignores drop out; anything the game would see fails;</item>
/// <item>PoE2: the saved JSON is identical to the original. The game's bundle parser needs
///   Unity, and exact equality is the stronger check anyway;</item>
/// <item>every stringtable in every installed language keeps every entry.</item>
/// </list>
/// These found #111–#116 in the shipped data; they are the regression net for them.
/// </remarks>
[Trait("Category", "GameData")]
public class ShippedConversationRoundTripTests(ITestOutputHelper output)
{
    private const int MaxReported = 25;

    // ── PoE1 ────────────────────────────────────────────────────────────────────

    [GameInstallFact(GameInstallFactAttribute.Poe1Variable)]
    public void Poe1_UnchangedSave_KeepsTheEditorModel() =>
        AssertEveryConversation(Poe1(), (provider, file) =>
        {
            var original = File.ReadAllText(file.ConversationPath);
            var saved    = Poe1ConversationSerializer.Serialize(original, UnchangedSnapshot(provider, file));
            return ModelDifference(Poe1ConversationParser.ParseXml(original), Poe1ConversationParser.ParseXml(saved));
        });

    [GameInstallFact(GameInstallFactAttribute.Poe1Variable)]
    public void Poe1_UnchangedSave_IsEquivalentInTheGamesOwnModel()
    {
        var gameSerializer = Poe1GameSerializer(GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe1Variable));

        AssertEveryConversation(Poe1(), (provider, file) =>
        {
            var original = File.ReadAllText(file.ConversationPath);
            var saved    = Poe1ConversationSerializer.Serialize(original, UnchangedSnapshot(provider, file));
            return FirstLineDifference(Poe1GameCanonical(gameSerializer, original), Poe1GameCanonical(gameSerializer, saved));
        });
    }

    [GameInstallFact(GameInstallFactAttribute.Poe1Variable)]
    public void Poe1_UnchangedStringTables_KeepEveryEntry() => AssertEveryStringTable(Poe1());

    // ── PoE2 ────────────────────────────────────────────────────────────────────

    [GameInstallFact(GameInstallFactAttribute.Poe2Variable)]
    public void Poe2_UnchangedSave_KeepsTheEditorModel() =>
        AssertEveryConversation(Poe2(), (provider, file) =>
        {
            var original = File.ReadAllText(file.ConversationPath);
            var saved    = Poe2ConversationSerializer.Serialize(original, UnchangedSnapshot(provider, file));
            return ModelDifference(Poe2ConversationParser.ParseJson(original), Poe2ConversationParser.ParseJson(saved));
        });

    [GameInstallFact(GameInstallFactAttribute.Poe2Variable)]
    public void Poe2_UnchangedSave_IsIdenticalJson() =>
        AssertEveryConversation(Poe2(), (provider, file) =>
        {
            var original = File.ReadAllText(file.ConversationPath);
            var saved    = Poe2ConversationSerializer.Serialize(original, UnchangedSnapshot(provider, file));
            return JsonDifference(original, saved);
        });

    [GameInstallFact(GameInstallFactAttribute.Poe2Variable)]
    public void Poe2_UnchangedStringTables_KeepEveryEntry() => AssertEveryStringTable(Poe2());

    // ── Harness ─────────────────────────────────────────────────────────────────

    private static Poe1GameDataProvider Poe1() =>
        new(GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe1Variable));

    private static Poe2GameDataProvider Poe2() =>
        new(GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe2Variable));

    /// <param name="check">Returns null when the conversation passes, else what differs.</param>
    private static void AssertEveryConversation(
        IGameDataProvider provider, Func<IGameDataProvider, ConversationFile, string?> check)
    {
        var files = provider.EnumerateConversations();
        Assert.NotEmpty(files);   // a wrong folder must not pass vacuously

        var failures = new List<string>();
        foreach (var file in files)
        {
            string? problem;
            try   { problem = check(provider, file); }
            catch (Exception ex) { problem = $"{ex.GetType().Name}: {ex.Message}"; }
            if (problem is not null) failures.Add($"{file.FolderPath}/{file.Name}: {problem}");
        }
        AssertNoFailures(failures, files.Count, "conversations");
    }

    private void AssertEveryStringTable(IGameDataProvider provider)
    {
        var files = provider.EnumerateConversations();
        Assert.NotEmpty(files);

        var failures   = new List<string>();
        var unreadable = new List<string>();
        var checkedCount = 0;
        foreach (var language in provider.AvailableLanguages)
        foreach (var file in files)
        {
            var path = provider.GetStringTablePath(file, language);
            if (!File.Exists(path)) continue;

            // An install file that is not XML at all (a GOG Deadfire install has one
            // binary German stringtable, #119) is not a round-trip failure: there is
            // nothing to round-trip. Reported, not failed. Only the ORIGINAL is exempt.
            string original;
            List<(int Id, string? DefaultText, string? FemaleText)> entries;
            try
            {
                original = File.ReadAllText(path);
                entries  = StringTableEntries(original);
            }
            catch (System.Xml.XmlException ex)
            {
                unreadable.Add($"{language}/{file.FolderPath}/{file.Name}: {ex.Message}");
                continue;
            }

            checkedCount++;
            try
            {
                var saved = StringTableSerializer.SerializeTranslations(original,
                    entries.Select(e => new NodeTranslation(e.Id, e.DefaultText, e.FemaleText)));
                if (!entries.SequenceEqual(StringTableEntries(saved)))
                    failures.Add($"{language}/{file.FolderPath}/{file.Name}: entries differ after save");
            }
            catch (Exception ex)
            {
                failures.Add($"{language}/{file.FolderPath}/{file.Name}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        foreach (var u in unreadable) output.WriteLine($"Skipped, not readable XML in the install: {u}");
        Assert.True(checkedCount > 0, "No stringtables found under the install.");
        AssertNoFailures(failures, checkedCount, "stringtables");
    }

    private static void AssertNoFailures(List<string> failures, int total, string what)
    {
        if (failures.Count == 0) return;
        var report = new StringBuilder($"{failures.Count} of {total} {what} did not round-trip:\n");
        foreach (var f in failures.Take(MaxReported)) report.AppendLine("  " + f);
        if (failures.Count > MaxReported) report.AppendLine($"  … and {failures.Count - MaxReported} more");
        Assert.Fail(report.ToString());
    }
}
