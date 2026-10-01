using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZWarden.Application.Agents;
using ZWarden.Application.Audit;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Web.Agents;
using ZWarden.Web.Tenancy;

namespace ZWarden.Web.Tests.Tenancy;

/// <summary>
/// #297 PR-A (ADR 0046 Q6): a circuit captures its tenant once, an Agent connection carries its tenant as a claim,
/// and every Agent hub scope gets that claim assigned. Each fails closed when the tenant is missing.
/// </summary>
public class CircuitAndHubTenantTests
{
    [Test]
    public async Task A_circuit_with_a_tenant_claim_captures_it_for_the_circuits_lifetime()
    {
        TenantId tenant = TenantId.New();
        await using ServiceProvider scope = SessionScope(SignedIn(tenant), out TenantAssignment assignment);
        TenantCircuitHandler handler = new(scope.GetRequiredService<AuthenticationStateProvider>(), assignment);

        await handler.OnCircuitOpenedAsync(null!, CancellationToken.None);

        await Assert.That(scope.GetRequiredService<ITenantContext>().CurrentTenantId).IsEqualTo(tenant);
    }

    [Test]
    public async Task A_circuit_without_a_tenant_claim_fails_closed()
    {
        await using ServiceProvider scope = SessionScope(new ClaimsPrincipal(new ClaimsIdentity()), out TenantAssignment assignment);
        TenantCircuitHandler handler = new(scope.GetRequiredService<AuthenticationStateProvider>(), assignment);

        await handler.OnCircuitOpenedAsync(null!, CancellationToken.None);

        ITenantContext context = scope.GetRequiredService<ITenantContext>();
        await Assert.That(context.HasCurrentTenant).IsFalse();
        await Assert.That(() => context.CurrentTenantId).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task The_hub_filter_assigns_the_agents_tenant_claim_into_the_hub_scope()
    {
        TenantId tenant = TenantId.New();
        await using ServiceProvider scope = SessionScope(new ClaimsPrincipal(new ClaimsIdentity()), out _);
        bool nextRan = false;

        await new AgentTenantHubFilter().OnConnectedAsync(
            new HubLifetimeContext(new FakeCaller(SignedIn(tenant)), scope, new NoOpHub()),
            _ =>
            {
                nextRan = true;
                return Task.CompletedTask;
            });

        await Assert.That(nextRan).IsTrue();
        await Assert.That(scope.GetRequiredService<ITenantContext>().CurrentTenantId).IsEqualTo(tenant);
    }

    [Test]
    public async Task The_hub_filter_refuses_a_connection_without_a_tenant_claim()
    {
        await using ServiceProvider scope = SessionScope(new ClaimsPrincipal(new ClaimsIdentity()), out _);
        ClaimsPrincipal agentOnly = new(new ClaimsIdentity(
            [new Claim(AgentClaims.AgentIdClaimType, AgentId.New().ToString())], AgentAuthenticationHandler.SchemeName));

        await Assert.That(() => AgentTenantHubFilter.Assign(scope, agentOnly)).Throws<HubException>();
    }

    [Test]
    public async Task The_agent_handshake_stamps_the_tenant_the_credential_was_verified_under()
    {
        AgentId agent = AgentId.New();
        ServiceCollection services = new();
        services.AddSessionTenantContext();
        services.AddTenantFoundation();
        services.AddSingleton<IAgentCredentialVerifier>(new FixedVerifier(agent));
        services.AddSingleton<IAuditWriter>(new NullAudit());
        await using ServiceProvider provider = services.BuildServiceProvider();
        // A non-default tenant proves the claim is the tenant the lookup ran under, not a constant.
        TenantId verifiedUnder = TenantId.New();
        await using AsyncServiceScope scope = provider.CreateTenantScope(verifiedUnder);

        DefaultHttpContext http = new() { RequestServices = scope.ServiceProvider };
        http.Request.Headers.Authorization = "Bearer some-credential";
        AgentAuthenticationHandler handler = new(
            new StaticOptions(), NullLoggerFactory.Instance, UrlEncoder.Default);
        await handler.InitializeAsync(
            new AuthenticationScheme(AgentAuthenticationHandler.SchemeName, null, typeof(AgentAuthenticationHandler)), http);

        AuthenticateResult result = await handler.AuthenticateAsync();

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Principal!.FindFirst(ClaimsPrincipalTenantContext.TenantClaimType)?.Value)
            .IsEqualTo(verifiedUnder.ToString());
    }

    private static ServiceProvider SessionScope(ClaimsPrincipal circuitUser, out TenantAssignment assignment)
    {
        ServiceCollection services = new();
        services.AddSessionTenantContext();
        services.AddTenantFoundation();
        services.AddScoped<AuthenticationStateProvider>(_ => new FixedAuthenticationState(circuitUser));
        // A single "scope" is enough here: build with scope validation off and resolve scoped services from the root.
        ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = false });
        assignment = provider.GetRequiredService<TenantAssignment>();
        return provider;
    }

    private static ClaimsPrincipal SignedIn(TenantId tenant) =>
        new(new ClaimsIdentity([new Claim(ClaimsPrincipalTenantContext.TenantClaimType, tenant.ToString())], "Test"));

    private sealed class FixedAuthenticationState(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(user));
    }

    private sealed class FixedVerifier(AgentId agent) : IAgentCredentialVerifier
    {
        public Task<AgentId?> VerifyAsync(SecretString presentedCredential, CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentId?>(agent);
    }

    private sealed class NullAudit : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StaticOptions : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();

        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }

    private sealed class NoOpHub : Hub;

    private sealed class FakeCaller(ClaimsPrincipal user) : HubCallerContext
    {
        public override string ConnectionId => "c1";

        public override string? UserIdentifier => null;

        public override ClaimsPrincipal? User => user;

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override Microsoft.AspNetCore.Http.Features.IFeatureCollection Features { get; } =
            new Microsoft.AspNetCore.Http.Features.FeatureCollection();

        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort()
        {
        }
    }
}
