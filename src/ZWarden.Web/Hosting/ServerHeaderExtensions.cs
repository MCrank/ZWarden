using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace ZWarden.Web.Hosting;

/// <summary>
/// #187: stops Kestrel disclosing the <c>Server: Kestrel</c> header through the Caddy front door (ADR 0035).
/// Minor stack disclosure that adds nothing for a control plane that only ever answers the proxy.
/// </summary>
public static class ServerHeaderExtensions
{
    /// <summary>Suppresses Kestrel's <c>Server</c> response header (equivalent to
    /// <c>ConfigureKestrel(o =&gt; o.AddServerHeader = false)</c>, expressed as an options mutation so it is
    /// unit-testable).</summary>
    public static IServiceCollection SuppressKestrelServerHeader(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure<KestrelServerOptions>(options => options.AddServerHeader = false);
        return services;
    }
}
