using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using ZWarden.Application.Agents;
using ZWarden.Application.Audit;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Agents;

namespace ZWarden.Web.Agents;

/// <summary>
/// Authenticates an Agent's SignalR connection at the handshake (F10; ADR 0007) by its F9 per-Agent
/// credential, <b>before</b> any hub method runs. The credential arrives as a SignalR access token — the
/// <c>access_token</c> query value on the WebSocket upgrade, or an <c>Authorization: Bearer</c> header on the
/// negotiate/long-poll — and is resolved by <see cref="IAgentCredentialVerifier"/> (trusted iff enabled and
/// matching, fail-closed). Success mints a principal carrying only the <see cref="AgentClaims.AgentIdClaimType"/>
/// claim; a bad credential fails (→ 401) and is audited with its reason server-side, never disclosed to the
/// Agent. A request with no token at all yields <see cref="AuthenticateResult.NoResult"/>, so ordinary
/// unauthenticated probes are a plain challenge, not an audit event.
/// </summary>
public sealed class AgentAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>The authentication scheme name the Agent hub authorizes against.</summary>
    public const string SchemeName = "Agent";

    private const string BearerPrefix = "Bearer ";

    public AgentAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? credential = ExtractCredential();
        if (string.IsNullOrEmpty(credential))
        {
            return AuthenticateResult.NoResult();
        }

        IAgentCredentialVerifier verifier = Context.RequestServices.GetRequiredService<IAgentCredentialVerifier>();
        AgentId? agentId = await verifier.VerifyAsync(new SecretString(credential), Context.RequestAborted)
            .ConfigureAwait(false);

        if (agentId is null)
        {
            await AuditRejectionAsync("invalid credential").ConfigureAwait(false);
            return AuthenticateResult.Fail("The presented Agent credential is not valid.");
        }

        ClaimsIdentity identity = new(
            [new Claim(AgentClaims.AgentIdClaimType, agentId.Value.ToString())],
            Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    private string? ExtractCredential()
    {
        // WebSocket transport cannot set headers, so SignalR passes the token as a query value; the
        // negotiate and the long-poll/SSE transports use the Authorization header.
        string? fromQuery = Request.Query["access_token"];
        if (!string.IsNullOrEmpty(fromQuery))
        {
            return fromQuery;
        }

        string authorization = Request.Headers.Authorization.ToString();
        return authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[BearerPrefix.Length..].Trim()
            : null;
    }

    private Task AuditRejectionAsync(string reason)
    {
        IAuditWriter audit = Context.RequestServices.GetRequiredService<IAuditWriter>();
        return audit.WriteAsync(
            new AuditEntry(AgentConnectionAuditActions.ConnectionRejected, AuditOutcome.Failed, null, null, reason),
            Context.RequestAborted);
    }
}
