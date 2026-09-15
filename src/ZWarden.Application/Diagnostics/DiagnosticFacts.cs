using ZWarden.Domain.Ids;

namespace ZWarden.Application.Diagnostics;

/// <summary>
/// The observed facts of the application database (F29), the pure input to the database evaluator. Gathered by
/// <see cref="IDiagnosticsDbProbe"/> against the live <c>ZWardenDbContext</c>.
/// </summary>
/// <param name="CanConnect">Whether the database answered a connectivity check.</param>
/// <param name="PendingMigrations">The number of EF Core migrations defined but not yet applied (0 when current).</param>
/// <param name="Provider">The active provider — e.g. <c>Sqlite</c> or <c>Npgsql</c> (ADR 0005).</param>
/// <param name="Error">An untrusted provider error message when the probe failed, else <c>null</c>.</param>
public sealed record DatabaseProbeFacts(
    bool CanConnect,
    int PendingMigrations,
    string Provider,
    string? Error = null);

/// <summary>
/// The observed facts of the configured public endpoint's serving TLS certificate (F29 D-3), the pure input to
/// the TLS evaluator. Gathered by <see cref="IDiagnosticsTlsProbe"/>. When no public HTTPS endpoint is configured
/// (an HTTP-only self-hosted deployment), <see cref="HttpsExpected"/> is <c>false</c> and the check is Skipped.
/// </summary>
/// <param name="HttpsExpected">Whether a public HTTPS endpoint is configured (else the domain is Skipped).</param>
/// <param name="Host">The configured public host the certificate was probed for (untrusted display value).</param>
/// <param name="CertificatePresent">Whether the endpoint presented a serving certificate.</param>
/// <param name="ChainValid">Whether the presented certificate chain validated.</param>
/// <param name="HostnameMatches">Whether the certificate matched <see cref="Host"/>.</param>
/// <param name="NotAfter">The certificate's expiry, or <c>null</c> when none was obtained.</param>
/// <param name="Error">An untrusted error message when the probe could not complete, else <c>null</c>.</param>
public sealed record TlsProbeFacts(
    bool HttpsExpected,
    string? Host,
    bool CertificatePresent,
    bool ChainValid,
    bool HostnameMatches,
    DateTimeOffset? NotAfter,
    string? Error = null);

/// <summary>
/// The observed presence facts of one enrolled Agent (F29), the pure input to the agent-connectivity evaluator.
/// <see cref="IsConnected"/> is the live per-process registry truth (F10); <see cref="LastSeenAt"/> is the
/// durable companion. Gathered by <see cref="IDiagnosticsAgentPresenceProbe"/>.
/// </summary>
/// <param name="AgentId">The Agent's id.</param>
/// <param name="Label">The Agent's operator label, if any (untrusted display value).</param>
/// <param name="IsEnabled">Whether the Agent's credential is enabled (a disabled Agent is not expected online).</param>
/// <param name="IsConnected">Whether the Agent holds a live connection to this Web process right now.</param>
/// <param name="LastSeenAt">When the Agent was last seen, or <c>null</c> if never.</param>
public sealed record AgentPresenceFacts(
    AgentId AgentId,
    string? Label,
    bool IsEnabled,
    bool IsConnected,
    DateTimeOffset? LastSeenAt);
