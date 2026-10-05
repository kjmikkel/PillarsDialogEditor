using System.Globalization;
using System.Text.Json.Nodes;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

/// <summary>
/// The one mapping between Deadfire's integer node enums and the editor's names, used
/// by both <see cref="Poe2ConversationParser"/> and the PoE2 serializer (issue 114).
/// </summary>
/// <remarks>
/// Each table is indexed by the game's own declaration order in OEIFormats, so the
/// integer is the array position. The parser and serializer used to keep separate
/// tables that shared the same off-by-one (Conversation read as 0, Bark as 1): values
/// 0 and 1 round-tripped, which hid the bug, while every real bark (2) and overlay (3)
/// was saved as Hidden and OncePerConversation / MarkAsRead were saved as None.
/// The names match what the PoE1 XML stores, so "Bark" means the same in both games.
/// "" means the property is absent (e.g. a BankNode has no DisplayType) and is left
/// out on save; a value the table does not know round-trips as "Unknown(n)".
/// </remarks>
internal static class Poe2EnumMaps
{
    // Shared with the detail pane's dropdowns (issue 132), so the two can never drift apart.
    private static readonly string[] DisplayTypes = [.. NodeEnumNames.DisplayTypes];
    private static readonly string[] Persistences = [.. NodeEnumNames.Persistences];

    private const string UnknownPrefix = "Unknown(";

    public static string DisplayTypeName(JsonNode? value) => Name(DisplayTypes, value);
    public static string PersistenceName(JsonNode? value) => Name(Persistences, value);

    /// <returns>The value to write, or <c>null</c> to leave the property out.</returns>
    public static int? DisplayTypeValue(string name) => Value(DisplayTypes, name);

    /// <returns>The value to write, or <c>null</c> to leave the property out.</returns>
    public static int? PersistenceValue(string name) => Value(Persistences, name);

    private static string Name(string[] names, JsonNode? value)
    {
        if (value is null) return string.Empty;
        var i = value.GetValue<int>();
        return i >= 0 && i < names.Length ? names[i] : $"{UnknownPrefix}{i})";
    }

    private static int? Value(string[] names, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        var i = Array.FindIndex(names, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) return i;

        if (name.StartsWith(UnknownPrefix, StringComparison.Ordinal) && name.EndsWith(')')
            && int.TryParse(name.AsSpan(UnknownPrefix.Length, name.Length - UnknownPrefix.Length - 1),
                            NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
            return raw;

        // A name neither game uses (e.g. free text from an import): leave the property
        // out so the game's default applies, rather than guessing a value.
        return null;
    }
}
