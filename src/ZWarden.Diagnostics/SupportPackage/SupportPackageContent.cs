using ZWarden.Application.Diagnostics;

namespace ZWarden.Diagnostics.SupportPackage;

/// <summary>
/// The raw, not-yet-sanitized input to the support-package pipeline (F30, PRD 51 "Collect"): the F29 diagnostic
/// report, the environment facts, a scope label, and the known host/player values the pseudonymizer should replace.
/// The Web-side collector (F30 PR-B) assembles this from <c>IDiagnosticsService</c> and the server inventory; the
/// pure <see cref="ISupportPackageBuilder"/> takes it from here and never performs I/O. Every string it carries out
/// of <see cref="Report"/> is untrusted (§8) and is sanitized/redacted/pseudonymized/scanned before it can be
/// packaged.
/// </summary>
/// <param name="Report">The transient F29 report to package.</param>
/// <param name="Environment">The non-secret environment facts.</param>
/// <param name="Scope">A short scope label — <c>tenant</c> or <c>server:&lt;pseudonym&gt;</c>.</param>
/// <param name="PseudonymTargets">Known host/player values to replace consistently (IPs are auto-detected).</param>
public sealed record SupportPackageContent(
    DiagnosticReport Report,
    EnvironmentFacts Environment,
    string Scope,
    IReadOnlyList<PseudonymTarget> PseudonymTargets)
{
    /// <summary>An empty target list — the common case where only IP detection is needed.</summary>
    public static IReadOnlyList<PseudonymTarget> NoTargets { get; } = [];
}
