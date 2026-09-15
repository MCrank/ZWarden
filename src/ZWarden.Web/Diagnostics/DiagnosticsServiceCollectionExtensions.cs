using System.Reflection;
using ZWarden.Application.Diagnostics;
using ZWarden.Diagnostics;
using ZWarden.Infrastructure.Diagnostics;

namespace ZWarden.Web.Diagnostics;

/// <summary>
/// Composition seam for the F29 diagnostics engine. Registers the tenant-wide <see cref="IDiagnosticsService"/>
/// (the read-only aggregating engine), its in-process probes, and the tunable <see cref="DiagnosticsOptions"/>
/// (bound from the <c>Diagnostics</c> configuration section, with the build version from the entry assembly). Call
/// it after <c>AddZWardenAuthorization</c>, <c>AddZWardenAudit</c>, <c>AddZWardenPersistence</c>, and
/// <c>AddZWardenEnrollment</c> — the service resolves the fail-closed <c>IPermissionChecker</c>, the
/// <c>IAuditWriter</c>, the <c>ZWardenDbContext</c>, and the <c>AgentRepository</c> + agent registry.
/// </summary>
public static class DiagnosticsServiceCollectionExtensions
{
    public static IServiceCollection AddZWardenDiagnostics(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        DiagnosticsOptions options = new()
        {
            Version = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            PublicHttpsUrl = configuration["Diagnostics:PublicHttpsUrl"],
        };
        services.AddSingleton(options);

        // In-process probes. DB + agent presence are scoped (they use the scoped DbContext / tenant-scoped
        // AgentRepository); the TLS probe is stateless.
        services.AddScoped<IDiagnosticsDbProbe, DiagnosticsDbProbe>();
        services.AddScoped<IDiagnosticsAgentPresenceProbe, DiagnosticsAgentPresenceProbe>();
        services.AddSingleton<IDiagnosticsTlsProbe, DiagnosticsTlsProbe>();

        services.AddScoped<IDiagnosticsService, DiagnosticsService>();

        return services;
    }
}
