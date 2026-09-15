using ZWarden.Application.Diagnostics;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Diagnostics;

namespace ZWarden.Infrastructure.Tests.Diagnostics;

/// <summary>
/// F29 PR-B: the in-memory gather cache. Host bundles are keyed by the reporting Agent; server bundles are
/// ownership-guarded — returned only to a caller naming the Server's true owning Agent (trust-boundaries §8), so a
/// bundle recorded for an unguessable ServerId cannot surface under the wrong Server.
/// </summary>
public class DiagnosticsResultCacheTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static DiagnosticBundle Bundle() =>
        new([new DiagnosticCheck(DiagnosticDomain.Docker, DiagnosticStatus.Pass, "ok")], At);

    [Test]
    public async Task A_host_bundle_is_returned_for_its_reporting_agent_and_not_others()
    {
        DiagnosticsResultCache cache = new();
        AgentId agent = AgentId.New();
        cache.RecordHost(agent, Bundle());

        await Assert.That(cache.GetHost(agent)).IsNotNull();
        await Assert.That(cache.GetHost(AgentId.New())).IsNull();
    }

    [Test]
    public async Task A_server_bundle_is_returned_only_to_its_owning_agent()
    {
        DiagnosticsResultCache cache = new();
        ServerId server = ServerId.New();
        AgentId owner = AgentId.New();
        cache.RecordServer(server, owner, Bundle());

        await Assert.That(cache.GetServer(server, owner)).IsNotNull();
        await Assert.That(cache.GetServer(server, AgentId.New())).IsNull(); // ownership guard
    }

    [Test]
    public async Task The_latest_write_per_key_wins()
    {
        DiagnosticsResultCache cache = new();
        AgentId agent = AgentId.New();
        cache.RecordHost(agent, Bundle());
        DiagnosticBundle newer = new([new DiagnosticCheck(DiagnosticDomain.Filesystem, DiagnosticStatus.Warn, "low")], At.AddMinutes(1));
        cache.RecordHost(agent, newer);

        await Assert.That(cache.GetHost(agent)!.ReportedAt).IsEqualTo(At.AddMinutes(1));
    }
}
