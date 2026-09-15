using ZWarden.Domain.Ids;

namespace ZWarden.Application.Diagnostics;

/// <summary>Why a diagnostics run was refused (F29). Fail-closed (ADR 0018): the tenant-wide
/// <c>Diagnostics.View</c> permission is checked before any probe runs.</summary>
public enum DiagnosticsRunFailure
{
    /// <summary>The caller lacks the tenant-wide <c>Diagnostics.View</c> permission.</summary>
    NotAuthorized,
}

/// <summary>The outcome of a diagnostics run (F29): on success the transient <see cref="DiagnosticReport"/>;
/// otherwise a typed failure. The report is not persisted (F29 D-2).</summary>
public sealed record DiagnosticsRunResult(bool Succeeded, DiagnosticReport? Report, DiagnosticsRunFailure? Failure)
{
    public static DiagnosticsRunResult Success(DiagnosticReport report) => new(true, report, null);

    public static DiagnosticsRunResult Denied(DiagnosticsRunFailure failure) => new(false, null, failure);
}

/// <summary>
/// The tenant-wide diagnostics engine (F29): run a read-only sweep across the diagnostic domains and return one
/// <see cref="DiagnosticReport"/>. Fail-closed (ADR 0018): it authorizes the tenant-wide <c>Diagnostics.View</c>
/// permission before probing anything, audits the run, and never mutates a Server or its world. In PR-A only the
/// in-process domains (Web, Database, TLS, Agent connectivity) produce live verdicts; the Agent-gathered domains
/// appear as <see cref="DiagnosticStatus.Skipped"/> until F29 PR-B/PR-C wire the Agent gather. The report is
/// transient — F30 owns the durable, redacted, packaged form.
/// </summary>
public interface IDiagnosticsService
{
    /// <summary>Runs a tenant-wide diagnostics sweep for <paramref name="user"/> — the platform domains (Web, DB,
    /// TLS, Agent connectivity) plus each connected Agent's cached host domains.</summary>
    Task<DiagnosticsRunResult> RunAsync(UserId user, CancellationToken cancellationToken = default);

    /// <summary>Assembles the per-server diagnostics report for <paramref name="serverId"/> — the server domains
    /// (RCON, game port, filesystem, SteamCMD, mods, config, compatibility) from the Server's cached gather plus
    /// the owning Agent's host domains (Docker, filesystem). <paramref name="owningAgentId"/> is the Server's true
    /// owning Agent (the caller resolves the Server through the tenant filter first); it guards the cache read.
    /// Read-only and transient; a domain with no cached gather yet is Skipped.</summary>
    Task<DiagnosticsRunResult> RunForServerAsync(
        UserId user, ServerId serverId, AgentId owningAgentId, CancellationToken cancellationToken = default);
}
