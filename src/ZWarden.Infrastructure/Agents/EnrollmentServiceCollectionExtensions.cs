using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZWarden.Application.Agents;
using ZWarden.Infrastructure.Security;

namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// Composition seam for Agent enrollment and trust (F9; ADR 0007). Registers the credential hasher, the
/// tenant-scoped repositories, and the four services — the operator-facing <see cref="IEnrollmentService"/>
/// and <see cref="IAgentTrustService"/>, the pre-trust <see cref="IAgentEnrollmentExchange"/>, and the
/// <see cref="IAgentCredentialVerifier"/> F10 will call. Call it after <c>AddZWardenAuthorization</c> and
/// <c>AddZWardenAudit</c> (the services resolve the fail-closed <c>IPermissionChecker</c>, the
/// <c>IAuditWriter</c>, the request-scoped <c>ZWardenDbContext</c> and its tenant, and the clock).
/// </summary>
public static class EnrollmentServiceCollectionExtensions
{
    public static IServiceCollection AddZWardenEnrollment(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<EnrollmentOptions>();

        // The hasher is stateless (RNG + SHA-256); a singleton is fine and confines the primitives here.
        services.TryAddSingleton<ICredentialHasher, CredentialHasher>();

        services.AddScoped<EnrollmentRepository>();
        services.AddScoped<AgentRepository>();

        services.AddScoped<IEnrollmentService, EnrollmentService>();
        services.AddScoped<IAgentTrustService, AgentTrustService>();
        services.AddScoped<IAgentEnrollmentExchange, AgentEnrollmentExchange>();
        services.AddScoped<IAgentCredentialVerifier, AgentCredentialVerifier>();

        // F10: the durable companion to the in-memory connection registry — stamps the Agent's observed
        // connection state (last-seen, connection state, negotiated version) as the hub sees it.
        services.AddScoped<IAgentConnectionStateWriter, AgentConnectionStateWriter>();

        return services;
    }
}
