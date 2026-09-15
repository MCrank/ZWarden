namespace ZWarden.Web.Hosting;

/// <summary>Registers the first-run <see cref="SetupGateMiddleware"/> (F33; ADR 0036).</summary>
public static class SetupGateExtensions
{
    /// <summary>Adds the first-run gate to the pipeline. Place it after antiforgery and before the endpoint
    /// mappings so browser navigations are redirected to <c>/setup</c> until setup is complete.</summary>
    public static IApplicationBuilder UseSetupGate(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<SetupGateMiddleware>();
    }
}
