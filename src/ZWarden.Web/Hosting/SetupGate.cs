using ZWarden.Application.Setup;
using ZWarden.Infrastructure.Setup;

namespace ZWarden.Web.Hosting;

/// <summary>
/// The first-run gate (F33; ADR 0036). While the installation's guided setup is not complete, browser
/// navigations are redirected to the <c>/setup</c> wizard so an operator cannot reach the app before there
/// is an administrator and a confirmed TLS mode. The wizard's own pages, framework/static assets, the
/// SignalR circuit, and the Agent hub are allowed through so setup can actually be performed. Completion is
/// monotonic and memoized in a process-wide <see cref="SetupCompletionSignal"/>, so once setup is done the
/// gate costs a single volatile read per request — no database round-trip.
/// </summary>
public sealed class SetupGateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SetupCompletionSignal _signal;

    public SetupGateMiddleware(RequestDelegate next, SetupCompletionSignal signal)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(signal);
        _next = next;
        _signal = signal;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Hot path once setup is done: a single volatile read, no scope, no query.
        if (_signal.IsComplete)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Always let the wizard, framework assets, the circuit and the Agent hub through so setup can run.
        if (IsAllowedDuringSetup(context.Request.Path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Not known-complete and not an allow-listed path: consult the store (which latches the signal the
        // first time it observes completion — e.g. after a headless env-admin boot on another instance).
        ISetupState setup = context.RequestServices.GetRequiredService<ISetupState>();
        if (await setup.IsSetupCompleteAsync(context.RequestAborted).ConfigureAwait(false))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Response.Redirect("/setup");
    }

    private static bool IsAllowedDuringSetup(PathString path)
    {
        if (path.StartsWithSegments("/setup", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/agent", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string value = path.HasValue ? path.Value! : "/";

        // Framework endpoints (/_blazor, /_framework, /_content), the error page, and any static asset
        // (a path with a file extension, incl. fingerprinted assets) must never be redirected.
        return value.StartsWith("/_", StringComparison.Ordinal)
            || value.StartsWith("/Error", StringComparison.OrdinalIgnoreCase)
            || Path.HasExtension(value);
    }
}
