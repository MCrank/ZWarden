namespace ZWarden.Application.Diagnostics;

/// <summary>
/// The result of one diagnostics sweep (F29): the per-domain <see cref="DiagnosticCheck"/>s and when the run was
/// taken. A run is <b>transient</b> (F29 D-2) — this report is surfaced to the operator and not persisted; F30
/// owns the durable, redacted, packaged form (its <c>DiagnosticId</c> and history). Every <see cref="DiagnosticCheck.Detail"/>
/// it carries is untrusted (§8) and must be escaped at render.
/// </summary>
/// <param name="Checks">The checks produced across the swept domains, in a stable domain order.</param>
/// <param name="RanAt">When the sweep was taken.</param>
public sealed record DiagnosticReport(IReadOnlyList<DiagnosticCheck> Checks, DateTimeOffset RanAt)
{
    /// <summary>The worst status across every check — the report's headline verdict. An empty report is
    /// <see cref="DiagnosticStatus.Skipped"/>.</summary>
    public DiagnosticStatus Worst()
    {
        DiagnosticStatus worst = DiagnosticStatus.Skipped;
        bool any = false;
        foreach (DiagnosticCheck check in Checks)
        {
            // Skipped is the neutral floor; a real verdict (Pass/Warn/Fail) always wins over it, then severity orders.
            if (!any || Severity(check.Status) > Severity(worst))
            {
                worst = check.Status;
            }

            any = true;
        }

        return worst;
    }

    // Skipped is neutral (lowest); then Pass < Warn < Fail. This is not the enum's ordinal order, so it is explicit.
    private static int Severity(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Skipped => 0,
        DiagnosticStatus.Pass => 1,
        DiagnosticStatus.Warn => 2,
        DiagnosticStatus.Fail => 3,
        _ => 0,
    };
}
