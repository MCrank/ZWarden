using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZWarden.Contracts.Protocol;
using ZWarden.Web.Observability;

namespace ZWarden.Web.Tests.Observability;

/// <summary>
/// F16 PR-B: the OpenTelemetry baseline registers cleanly whether or not an OTLP endpoint is configured (opt-in
/// egress), and the ZWarden-owned control-plane metrics resolve and record without a live collector.
/// </summary>
public class TelemetryRegistrationTests
{
    [Test]
    [Arguments(null)]
    [Arguments("http://localhost:4317")]
    public async Task Telemetry_registers_and_control_plane_metrics_resolve(string? otlpEndpoint)
    {
        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(Config(otlpEndpoint));
        services.AddZWardenTelemetry(Config(otlpEndpoint), new StubEnvironment("Production"));

        await using ServiceProvider provider = services.BuildServiceProvider();
        ControlPlaneMetrics metrics = provider.GetRequiredService<ControlPlaneMetrics>();

        // Recording must not throw with or without a collector wired.
        metrics.RecordHealthTransition(ServerHealth.Degraded);
        await Assert.That(metrics).IsNotNull();
    }

    [Test]
    public async Task Development_adds_the_console_exporter_without_throwing()
    {
        ServiceCollection services = new();
        services.AddZWardenTelemetry(Config(null), new StubEnvironment("Development"));

        await using ServiceProvider provider = services.BuildServiceProvider();

        await Assert.That(provider.GetRequiredService<ControlPlaneMetrics>()).IsNotNull();
    }

    private static IConfiguration Config(string? otlpEndpoint) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ZWarden:Observability:OtlpEndpoint"] = otlpEndpoint,
            })
            .Build();

    private sealed class StubEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "ZWarden.Web.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
