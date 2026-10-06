using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Health;
using ZWarden.Agent.Identity;
using ZWarden.Contracts.Protocol;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests;

/// <summary>
/// F8 test plan items 6-7: the worker starts, runs and shuts down gracefully, and its startup banner
/// reports the Agent identity and protocol version without leaking a secret.
/// </summary>
public class AgentWorkerTests
{
    private static (AgentWorker Worker, AgentHealthState Health, RecordingLogger<AgentWorker> Logger) Build(
        IAgentIdentity identity,
        AgentOptions? options = null)
    {
        var health = new AgentHealthState(new RecordingLogger<AgentHealthState>());
        var logger = new RecordingLogger<AgentWorker>();
        options ??= new AgentOptions
        {
            ControlPlaneUri = "https://cp.example:8443",
            HeartbeatInterval = TimeSpan.FromSeconds(30),
            HealthReportInterval = TimeSpan.FromMilliseconds(20),
            ShutdownTimeout = TimeSpan.FromSeconds(5),
        };

        var worker = new AgentWorker(
            identity,
            health,
            Options.Create(options),
            TimeProvider.System,
            logger);

        return (worker, health, logger);
    }

    [Test]
    public async Task Starts_healthy_and_stops_gracefully()
    {
        AgentId id = AgentId.New();
        var (worker, health, _) = Build(new FixedAgentIdentity(id));

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(60); // let the liveness loop run a couple of ticks
        await Assert.That(health.Current).IsEqualTo(AgentHealthStatus.Healthy);

        // A clean stop must not throw and must leave the runtime marked not-serving.
        await worker.StopAsync(CancellationToken.None);
        await Assert.That(health.Current).IsEqualTo(AgentHealthStatus.Unhealthy);

        worker.Dispose();
    }

    [Test]
    public async Task Startup_banner_reports_identity_and_protocol_version()
    {
        AgentId id = AgentId.New();
        var (worker, _, logger) = Build(new FixedAgentIdentity(id));

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        var banner = logger.Entries.FirstOrDefault(e => e.Message.Contains("ZWarden.Agent starting", StringComparison.Ordinal));
        await Assert.That(banner).IsNotNull();
        await Assert.That(banner!.Message).Contains(id.ToString());
        await Assert.That(banner.Message).Contains(ProtocolVersion.Current.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(banner.Message).Contains("https://cp.example:8443");

        // No secret is rendered in any log line (there is none in F8; this guards the pattern for F9).
        foreach (var entry in logger.Entries)
        {
            await Assert.That(entry.Message).DoesNotContain("password", StringComparison.OrdinalIgnoreCase);
        }

        worker.Dispose();
    }

    [Test]
    public async Task Once_enrolled_the_banner_names_the_enrolled_id_and_traces_the_local_one()
    {
        // #365: the logs carry the AgentId the Hosts card shows; the local id is logged once so older log lines
        // (and containers stamped with it) stay traceable.
        AgentId local = AgentId.New();
        AgentId enrolled = AgentId.New();
        AgentIdentityHolder holder = new();
        holder.Set(local);
        holder.MarkEnrolled(enrolled);
        var (worker, _, logger) = Build(holder);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        var banner = logger.Entries.First(e => e.Message.Contains("ZWarden.Agent starting", StringComparison.Ordinal));
        await Assert.That(banner.Message).Contains(enrolled.ToString());
        await Assert.That(banner.Message).DoesNotContain(local.ToString());
        var trace = logger.Entries.Where(e => e.Message.Contains(local.ToString(), StringComparison.Ordinal)).ToList();
        await Assert.That(trace.Count).IsEqualTo(1);
        await Assert.That(trace[0].Message).Contains(enrolled.ToString());
        var stopping = logger.Entries.First(e => e.Message.Contains("ZWarden.Agent stopping", StringComparison.Ordinal));
        await Assert.That(stopping.Message).Contains(enrolled.ToString());

        worker.Dispose();
    }

    [Test]
    public async Task Before_enrollment_only_the_local_id_is_logged()
    {
        AgentId local = AgentId.New();
        AgentIdentityHolder holder = new();
        holder.Set(local);
        var (worker, _, logger) = Build(holder);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        await Assert.That(logger.Entries.Count(e => e.Message.Contains("local id", StringComparison.Ordinal))).IsEqualTo(0);
        var banner = logger.Entries.First(e => e.Message.Contains("ZWarden.Agent starting", StringComparison.Ordinal));
        await Assert.That(banner.Message).Contains(local.ToString());

        worker.Dispose();
    }
}
