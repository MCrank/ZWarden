namespace ZWarden.Application.Diagnostics;

/// <summary>
/// Tunables for the diagnostics engine (F29): the reported build version and the evaluator thresholds. Registered
/// as a singleton; defaults match the evaluators' own defaults.
/// </summary>
public sealed class DiagnosticsOptions
{
    /// <summary>The build version reported by the Web base health check, if known.</summary>
    public string? Version { get; init; }

    /// <summary>
    /// The public HTTPS URL whose serving certificate the TLS check probes (F29 D-3). When unset — or not an
    /// <c>https</c> URL — the deployment is treated as HTTP-only and the TLS check is Skipped. F32's Caddy
    /// reference deployment points this at the public endpoint; a self-hosted HTTP deployment leaves it unset.
    /// </summary>
    public string? PublicHttpsUrl { get; init; }

    /// <summary>How long before certificate expiry the TLS check warns (default 14 days).</summary>
    public TimeSpan TlsWarnWindow { get; init; } = TimeSpan.FromDays(14);

    /// <summary>How long after an Agent's last-seen a disconnected Agent is treated as offline (default 5 minutes).</summary>
    public TimeSpan AgentStaleWindow { get; init; } = TimeSpan.FromMinutes(5);
}
