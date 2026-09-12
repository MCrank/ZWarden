using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Health;
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
        AgentId agentId,
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
            new FixedAgentIdentity(agentId),
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
        var (worker, health, _) = Build(id);

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
        var (worker, _, logger) = Build(id);

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
}
