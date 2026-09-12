// ZWarden.Agent host entry point (F8 — Agent Runtime Skeleton).
//
// Two-stage Serilog bootstrap (ADR 0021): a bootstrap logger captures failures during host
// construction, then the host-integrated logger takes over from configuration. The runtime proper
// — self-identity, validated configuration, health state, graceful shutdown and diagnostics — is
// composed by AddAgentRuntime. Transport (F10), enrollment (F9), Docker (F13) and RCON (F18) are
// deliberately absent.
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using ZWarden.Agent;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((services, configuration) => configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddAgentRuntime(builder.Configuration);

    IHost host = builder.Build();
    host.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "ZWarden.Agent host terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
