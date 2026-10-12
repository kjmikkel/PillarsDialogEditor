using System.Security.Cryptography;
using System.Text;

namespace DialogEditor.Core.GameData;

/// The ID a conversation created in the editor gets in the game (issue 162): an RFC 9562
/// version 5 UUID of its lower-cased name. Deadfire finds a conversation by the ID in its
/// manifest entry and bundle. The patcher rebuilds new conversations on every install, and
/// save games record conversation IDs, so the ID must come out the same every time: derived,
/// never random.
public static class NewConversationId
{
    // NEVER CHANGE: every installed new conversation's ID, and any save game that refers to
    // one, depends on it. NewConversationIdTests pins golden values.
    private static readonly Guid Namespace = new("eed25f96-1230-4e6b-a99f-dd8956046f4f");

    public static Guid For(string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name.ToLowerInvariant());
        var input     = new byte[16 + nameBytes.Length];
        Namespace.TryWriteBytes(input.AsSpan(0, 16), bigEndian: true, out _);
        nameBytes.CopyTo(input, 16);

        var hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);   // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);   // RFC variant
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
