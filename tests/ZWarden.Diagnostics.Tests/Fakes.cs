using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Diagnostics;
using ZWarden.Diagnostics.SupportPackage;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;

namespace ZWarden.Diagnostics.Tests;

/// <summary>Test doubles for the F29 engine service: they let a test drive authorization, the probe facts, the
/// clock, and observe the audit trail without any database, registry, or network.</summary>
internal sealed class FakePermissionChecker : IPermissionChecker
{
    private readonly bool _allow;

    public FakePermissionChecker(bool allow) => _allow = allow;

    public Task<AuthorizationDecision> EvaluateAsync(
        UserId user, PermissionDefinition permission, ServerId? server = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(_allow ? AuthorizationDecision.Allow(permission, server) : AuthorizationDecision.Deny("test-deny"));

    public Task<IReadOnlySet<string>> GetTenantWidePermissionsAsync(UserId user, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
}

internal sealed class FakeDbProbe : IDiagnosticsDbProbe
{
    private readonly DatabaseProbeFacts _facts;
    public bool WasCalled { get; private set; }

    public FakeDbProbe(DatabaseProbeFacts facts) => _facts = facts;

    public Task<DatabaseProbeFacts> ProbeAsync(CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return Task.FromResult(_facts);
    }
}

internal sealed class FakeTlsProbe : IDiagnosticsTlsProbe
{
    private readonly TlsProbeFacts _facts;

    public FakeTlsProbe(TlsProbeFacts facts) => _facts = facts;

    public Task<TlsProbeFacts> ProbeAsync(CancellationToken cancellationToken = default) => Task.FromResult(_facts);
}

internal sealed class FakeAgentPresenceProbe : IDiagnosticsAgentPresenceProbe
{
    private readonly IReadOnlyList<AgentPresenceFacts> _facts;

    public FakeAgentPresenceProbe(IReadOnlyList<AgentPresenceFacts> facts) => _facts = facts;

    public Task<IReadOnlyList<AgentPresenceFacts>> ProbeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_facts);
}

internal sealed class FakeDiagnosticsResultCache : IDiagnosticsResultCache
{
    private readonly Dictionary<AgentId, DiagnosticBundle> _host = [];
    private readonly Dictionary<ServerId, (AgentId Owner, DiagnosticBundle Bundle)> _server = [];

    public void RecordHost(AgentId agentId, DiagnosticBundle bundle) => _host[agentId] = bundle;

    public DiagnosticBundle? GetHost(AgentId agentId) => _host.TryGetValue(agentId, out DiagnosticBundle? b) ? b : null;

    public void RecordServer(ServerId serverId, AgentId owningAgentId, DiagnosticBundle bundle) =>
        _server[serverId] = (owningAgentId, bundle);

    public DiagnosticBundle? GetServer(ServerId serverId, AgentId owningAgentId) =>
        _server.TryGetValue(serverId, out (AgentId Owner, DiagnosticBundle Bundle) e) && e.Owner == owningAgentId
            ? e.Bundle
            : null;
}

internal sealed class CapturingAuditWriter : IAuditWriter
{
    public List<AuditEntry> Entries { get; } = [];

    public List<string> Actions => Entries.ConvertAll(e => e.Action);

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

/// <summary>A <see cref="TimeProvider"/> pinned to a fixed instant.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>A stand-in <see cref="IDiagnosticsService"/> returning preset run results (F30 service tests).</summary>
internal sealed class FakeDiagnosticsService : IDiagnosticsService
{
    private readonly DiagnosticsRunResult _tenant;
    private readonly DiagnosticsRunResult _server;

    public FakeDiagnosticsService(DiagnosticsRunResult tenant, DiagnosticsRunResult? server = null)
    {
        _tenant = tenant;
        _server = server ?? tenant;
    }

    public bool TenantRun { get; private set; }
    public bool ServerRun { get; private set; }

    public Task<DiagnosticsRunResult> RunAsync(UserId user, CancellationToken cancellationToken = default)
    {
        TenantRun = true;
        return Task.FromResult(_tenant);
    }

    public Task<DiagnosticsRunResult> RunForServerAsync(
        UserId user, ServerId serverId, AgentId owningAgentId, CancellationToken cancellationToken = default)
    {
        ServerRun = true;
        return Task.FromResult(_server);
    }
}

/// <summary>A fixed <see cref="IEnvironmentFactsProvider"/> for the F30 support-package tests.</summary>
internal sealed class FakeEnvironmentFactsProvider : IEnvironmentFactsProvider
{
    private readonly EnvironmentFacts _facts;

    public FakeEnvironmentFactsProvider(EnvironmentFacts facts) => _facts = facts;

    public EnvironmentFacts Capture() => _facts;
}
