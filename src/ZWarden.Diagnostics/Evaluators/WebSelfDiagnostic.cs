using ZWarden.Application.Diagnostics;

namespace ZWarden.Diagnostics.Evaluators;

/// <summary>
/// The base health check (F29; PRD 50 "Test Web"): the diagnostics engine runs inside the ZWarden.Web host, so
/// reaching this evaluator is itself proof the host is responding. A <b>pure</b> function that always
/// <see cref="DiagnosticStatus.Pass"/>es, carrying the build version as detail for context.
/// </summary>
public static class WebSelfDiagnostic
{
    /// <summary>Evaluates the Web self-check into a <see cref="DiagnosticDomain.Web"/> check.</summary>
    public static DiagnosticCheck Evaluate(string? version)
    {
        string? detail = version is { Length: > 0 } ? $"version {version}" : null;
        return DiagnosticCheck.Create(DiagnosticDomain.Web, DiagnosticStatus.Pass, "ZWarden.Web is responding.", detail);
    }
}
