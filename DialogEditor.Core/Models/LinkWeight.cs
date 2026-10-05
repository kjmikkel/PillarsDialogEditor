namespace DialogEditor.Core.Models;

/// <summary>
/// Link random weights as the games store them (issue 129). The editor carries weights as
/// <c>float</c> (projects saved before this fix may hold fractions), but both games declare
/// <c>DialogueLink.RandomWeight</c> as <c>int</c>: PoE1's XmlSerializer and PoE2's
/// <c>ReadAsInt32</c> both fail on "1.5", and the whole conversation does not load.
/// </summary>
public static class LinkWeight
{
    /// The whole number the game will read for <paramref name="weight"/>. The games pick a
    /// link by cumulative integer weight, so 0 means "never chosen": a positive weight below
    /// 1 becomes 1, because it was meant as "rarely", not "never". Otherwise halves round
    /// away from zero, and negative weights become 0.
    public static int ToGame(float weight) => weight switch
    {
        <= 0f => 0,
        < 1f  => 1,
        _     => (int)MathF.Round(weight, MidpointRounding.AwayFromZero),
    };
}
