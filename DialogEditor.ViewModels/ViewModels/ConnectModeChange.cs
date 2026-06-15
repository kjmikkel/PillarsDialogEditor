namespace DialogEditor.ViewModels;

/// <summary>
/// The three transitions of keyboard "connect mode" (Gaps.md Accessibility item 4
/// follow-up — see docs/superpowers/specs/2026-06-15-connect-mode-design.md).
/// </summary>
public enum ConnectModeChange
{
    Started,
    Connected,
    Cancelled,
}
