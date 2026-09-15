using ZWarden.Application.Diagnostics;
using ZWarden.Domain.Ids;

namespace ZWarden.Diagnostics.Tests;

/// <summary>
/// F29 PR-A: the engine service. It is fail-closed (ADR 0018) — a caller without tenant-wide
/// <c>Diagnostics.View</c> gets nothing and no probe runs — and, when authorized, assembles one transient report
/// of the in-process domain verdicts plus Skipped placeholders for the Agent-gathered domains, and audits the run.
/// Proven here with fake probes, a fake permission checker, and a fixed clock — no database, registry, or network.
/// </summary>
public class DiagnosticsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly DatabaseProbeFacts HealthyDb = new(CanConnect: true, PendingMigrations: 0, Provider: "Sqlite");
    private static readonly TlsProbeFacts HttpOnlyTls =
        new(HttpsExpected: false, Host: null, CertificatePresent: false, ChainValid: false, HostnameMatches: false, NotAfter: null);

    private static DiagnosticsService Service(
        bool allow,
        DatabaseProbeFacts? db = null,
        TlsProbeFacts? tls = null,
        IReadOnlyList<AgentPresenceFacts>? agents = null,
        FakeDbProbe? dbProbe = null,
        CapturingAuditWriter? audit = null,
        IDiagnosticsResultCache? cache = null,
        DiagnosticsOptions? options = null) =>
        new(
            new FakePermissionChecker(allow),
            dbProbe ?? new FakeDbProbe(db ?? HealthyDb),
            new FakeTlsProbe(tls ?? HttpOnlyTls),
            new FakeAgentPresenceProbe(agents ?? []),
            cache ?? new FakeDiagnosticsResultCache(),
            audit ?? new CapturingAuditWriter(),
            new FixedTimeProvider(Now),
            options ?? new DiagnosticsOptions());

    [Test]
    public async Task An_unauthorized_caller_is_denied_and_no_probe_runs()
    {
        FakeDbProbe dbProbe = new(HealthyDb);
        CapturingAuditWriter audit = new();
        DiagnosticsService sut = Service(allow: false, dbProbe: dbProbe, audit: audit);

        DiagnosticsRunResult result = await sut.RunAsync(UserId.New());

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(DiagnosticsRunFailure.NotAuthorized);
        await Assert.That(result.Report).IsNull();
        await Assert.That(dbProbe.WasCalled).IsFalse(); // fail-closed: nothing is probed before authorization
        await Assert.That(audit.Entries).IsEmpty();
    }

    [Test]
    public async Task An_authorized_run_returns_a_report_covering_every_domain()
    {
        DiagnosticsService sut = Service(allow: true);

        DiagnosticsRunResult result = await sut.RunAsync(UserId.New());

        await Assert.That(result.Succeeded).IsTrue();
        DiagnosticReport report = result.Report!;
        // Every diagnostic domain appears exactly once (in-process verdicts + Skipped placeholders).
        foreach (DiagnosticDomain domain in Enum.GetValues<DiagnosticDomain>())
        {
            await Assert.That(report.Checks.Count(c => c.Domain == domain)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task The_agent_gathered_domains_are_skipped_in_pr_a()
    {
        DiagnosticsService sut = Service(allow: true);

        DiagnosticReport report = (await sut.RunAsync(UserId.New())).Report!;

        foreach (DiagnosticDomain domain in new[]
                 {
                     DiagnosticDomain.Docker, DiagnosticDomain.Rcon, DiagnosticDomain.GamePort, DiagnosticDomain.Filesystem,
                     DiagnosticDomain.SteamCmd, DiagnosticDomain.Mod, DiagnosticDomain.Config, DiagnosticDomain.Compatibility,
                 })
        {
            DiagnosticCheck check = report.Checks.Single(c => c.Domain == domain);
            await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Skipped);
        }
    }

    [Test]
    public async Task The_web_self_check_always_passes()
    {
        DiagnosticsService sut = Service(allow: true, options: new DiagnosticsOptions { Version = "9.9.9" });

        DiagnosticReport report = (await sut.RunAsync(UserId.New())).Report!;

        DiagnosticCheck web = report.Checks.Single(c => c.Domain == DiagnosticDomain.Web);
        await Assert.That(web.Status).IsEqualTo(DiagnosticStatus.Pass);
        await Assert.That(web.Detail).Contains("9.9.9");
    }

    [Test]
    public async Task An_unreachable_database_surfaces_as_a_failed_check()
    {
        DiagnosticsService sut = Service(
            allow: true,
            db: new DatabaseProbeFacts(CanConnect: false, PendingMigrations: 0, Provider: "Npgsql", Error: "refused"));

        DiagnosticReport report = (await sut.RunAsync(UserId.New())).Report!;

        DiagnosticCheck database = report.Checks.Single(c => c.Domain == DiagnosticDomain.Database);
        await Assert.That(database.Status).IsEqualTo(DiagnosticStatus.Fail);
        await Assert.That(report.Worst()).IsEqualTo(DiagnosticStatus.Fail);
    }

    [Test]
    public async Task An_expiring_certificate_surfaces_as_a_warn_using_the_configured_window()
    {
        TlsProbeFacts expiring = new(
            HttpsExpected: true, Host: "zwarden.example", CertificatePresent: true, ChainValid: true, HostnameMatches: true,
            NotAfter: Now.AddDays(3));
        DiagnosticsService sut = Service(allow: true, tls: expiring, options: new DiagnosticsOptions { TlsWarnWindow = TimeSpan.FromDays(7) });

        DiagnosticReport report = (await sut.RunAsync(UserId.New())).Report!;

        DiagnosticCheck tls = report.Checks.Single(c => c.Domain == DiagnosticDomain.Tls);
        await Assert.That(tls.Status).IsEqualTo(DiagnosticStatus.Warn);
    }

    [Test]
    public async Task A_connected_agents_cached_host_bundle_replaces_the_skipped_host_domain()
    {
        AgentId agent = AgentId.New();
        FakeDiagnosticsResultCache cache = new();
        cache.RecordHost(agent, new DiagnosticBundle(
            [new DiagnosticCheck(DiagnosticDomain.Docker, DiagnosticStatus.Pass, "Docker is reachable.")], Now));
        AgentPresenceFacts[] agents = [new AgentPresenceFacts(agent, null, IsEnabled: true, IsConnected: true, LastSeenAt: Now)];
        DiagnosticsService sut = Service(allow: true, agents: agents, cache: cache);

        DiagnosticReport report = (await sut.RunAsync(UserId.New())).Report!;

        DiagnosticCheck docker = report.Checks.Single(c => c.Domain == DiagnosticDomain.Docker);
        await Assert.That(docker.Status).IsEqualTo(DiagnosticStatus.Pass); // merged from the cache, not the Skipped placeholder
    }

    [Test]
    public async Task An_authorized_run_is_audited()
    {
        CapturingAuditWriter audit = new();
        DiagnosticsService sut = Service(allow: true, audit: audit);

        await sut.RunAsync(UserId.New());

        await Assert.That(audit.Actions).Contains(DiagnosticsAuditActions.Run);
    }
}
