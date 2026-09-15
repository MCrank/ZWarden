using Microsoft.AspNetCore.HttpOverrides;

namespace ZWarden.Web.Hosting;

/// <summary>
/// F32 / ADR 0035: configures forwarded-headers processing so ZWarden.Web trusts the reference Caddy ingress
/// that terminates TLS in front of it. Behind the proxy the original scheme, host, and client IP arrive in
/// X-Forwarded-Proto/Host/For; without honouring them <c>UseHttpsRedirection</c> would loop on an already-HTTPS
/// request and secure cookies would be mis-scoped. Trust rests on the PRD §45 invariant that ZWarden.Web is
/// never exposed to the Internet directly — only the ingress can reach it — with ADR 0006 host filtering still
/// validating the (now forwarded) Host header.
/// </summary>
public static class ForwardedHeadersExtensions
{
    /// <summary>
    /// Registers <see cref="ForwardedHeadersOptions"/> that honour X-Forwarded-Proto/Host/For and trust the
    /// forwarding proxy regardless of its container-assigned address. Pair with <c>app.UseForwardedHeaders()</c>
    /// placed first in the request pipeline (before host filtering and HTTPS redirection).
    /// </summary>
    public static IServiceCollection AddProxyForwardedHeaders(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

            // The default allowlists trust only loopback, which would make the middleware ignore the ingress's
            // forwarded headers. Clearing them trusts the proxy — safe because only the proxy can reach Web
            // (PRD §45), and the Host header is still validated by host filtering (ADR 0006).
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }
}
