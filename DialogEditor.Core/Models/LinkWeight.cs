namespace DialogEditor.Core.Models;

/// <summary>
/// Link random weights (issue 129). Both games declare <c>DialogueLink.RandomWeight</c> as
/// <c>int</c> — PoE1's XmlSerializer and PoE2's <c>ReadAsInt32</c> fail on "1.5" and the
/// whole conversation does not load — so the editor carries weights as <c>int</c> too. The
/// one place a fraction can still come from is a number typed into the weight field.
/// </summary>
public static class LinkWeight
{
    /// The whole weight for a typed <paramref name="value"/>. The games pick a link by
    /// cumulative integer weight, so 0 means "never chosen": a positive value below 1 becomes
    /// 1, because it was meant as "rarely", not "never". Otherwise halves round away from
    /// zero, and negative values become 0.
    public static int FromInput(decimal value) => value switch
    {
        <= 0m => 0,
        < 1m  => 1,
        _     => (int)Math.Round(value, MidpointRounding.AwayFromZero),
    };
}
