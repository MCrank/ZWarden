using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZWarden.Agent.Observability;

namespace ZWarden.Agent.Tests.Observability;

/// <summary>
/// F16 PR-B: the Agent's OpenTelemetry baseline registers cleanly with OTLP off (default) and on (opt-in), and
/// the Agent metrics resolve and record without a live collector.
/// </summary>
public class AgentTelemetryRegistrationTests
{
    [Test]
    [Arguments(null)]
    [Arguments("http://localhost:4317")]
    public async Task Telemetry_registers_and_agent_metrics_resolve(string? otlpEndpoint)
    {
        ServiceCollection services = new();
        services.AddAgentTelemetry(Config(otlpEndpoint), new StubEnvironment("Production"));

        await using ServiceProvider provider = services.BuildServiceProvider();
        AgentMetrics metrics = provider.GetRequiredService<AgentMetrics>();

        metrics.RecordMetricsSweep(3);
        await Assert.That(metrics).IsNotNull();
    }

    private static IConfiguration Config(string? otlpEndpoint) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:Observability:OtlpEndpoint"] = otlpEndpoint,
            })
            .Build();

    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "ZWarden.Agent.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
