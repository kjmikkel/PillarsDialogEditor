namespace DialogEditor.Core.Conditions;

/// <summary>
/// Literal transcriptions of the two games' left-to-right condition folds over already-known
/// bool results. There is no AND/OR precedence; each component's operator links it to the next.
/// </summary>
internal static class ConditionFold
{
    /// Mirrors PoE2 ScriptManager.EvaluateExpressionWithOwnerObjects.
    public static bool Poe2(IReadOnlyList<(bool Result, bool NextIsAnd)> c)
    {
        if (c.Count == 0) return true;
        bool prevIsAnd = false; // prev = Or
        bool flag = false;
        for (int i = 0; i < c.Count; i++)
        {
            var (r, nextIsAnd) = c[i];
            if (i == c.Count - 1)
                return prevIsAnd ? r : (r || flag);
            if (nextIsAnd && !r) return false;
            if (!nextIsAnd && r) return true;
            flag = r;
            prevIsAnd = nextIsAnd;
        }
        return false;
    }

    /// Mirrors PoE1 FlowChart.EvaluateExpression.
    public static bool Poe1(IReadOnlyList<(bool Result, bool NextIsAnd)> c)
    {
        if (c.Count == 0) return true;
        bool prevIsAnd = false; // prev = Or
        bool flag = false;
        for (int i = 0; i < c.Count; i++)
        {
            var (r, nextIsAnd) = c[i];
            if (i == c.Count - 1)
                return prevIsAnd ? r : (r || flag);
            if (i == 0)
            {
                if (nextIsAnd && !r) return false;
                if (!nextIsAnd && r) return true;
            }
            else if (prevIsAnd)
            {
                if (!r) return false;
                if (!nextIsAnd) return true;
            }
            else
            {
                if (r) return true;
                if (nextIsAnd) return false;
            }
            flag = r;
            prevIsAnd = nextIsAnd;
        }
        return false;
    }
}
