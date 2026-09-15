namespace ZWarden.Application.Diagnostics;

/// <summary>
/// Gathers the application database's <see cref="DatabaseProbeFacts"/> (F29) — a read-only connectivity and
/// migration-state check against the live <c>ZWardenDbContext</c>. The impl lives in Infrastructure (the only
/// layer that holds the DbContext); the pure verdict is <c>DatabaseDiagnostic</c>'s.
/// </summary>
public interface IDiagnosticsDbProbe
{
    /// <summary>Reads the database's connectivity and pending-migration facts. Never throws for an expected
    /// database fault — a fault is reported as <see cref="DatabaseProbeFacts.CanConnect"/> = false with the error.</summary>
    Task<DatabaseProbeFacts> ProbeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Gathers the configured public endpoint's serving-certificate <see cref="TlsProbeFacts"/> (F29 D-3) — a
/// read-only probe of the public URL. The impl lives in Web (it knows the configured public URL and performs the
/// outbound TLS handshake); the pure verdict is <c>TlsDiagnostic</c>'s. mTLS is out (v1.1, ADR 0007).
/// </summary>
public interface IDiagnosticsTlsProbe
{
    /// <summary>Reads the serving-certificate facts. When no public HTTPS endpoint is configured, returns facts
    /// with <see cref="TlsProbeFacts.HttpsExpected"/> = false (the check is then Skipped). Never throws for an
    /// expected connection fault — it is reported as <see cref="TlsProbeFacts.Error"/>.</summary>
    Task<TlsProbeFacts> ProbeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Gathers the enrolled Agents' <see cref="AgentPresenceFacts"/> (F29) — combining the live per-process
/// connection registry (F10) with each Agent's persisted last-seen. The impl lives in Web (it holds the
/// registry); the pure rollup is <c>AgentConnectivityDiagnostic</c>'s.
/// </summary>
public interface IDiagnosticsAgentPresenceProbe
{
    /// <summary>Reads presence facts for every enrolled Agent in the current tenant.</summary>
    Task<IReadOnlyList<AgentPresenceFacts>> ProbeAsync(CancellationToken cancellationToken = default);
}
