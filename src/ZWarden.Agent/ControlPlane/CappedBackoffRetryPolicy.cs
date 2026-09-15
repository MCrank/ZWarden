using Microsoft.AspNetCore.SignalR.Client;

namespace ZWarden.Agent.ControlPlane;

/// <summary>
/// The reconnect policy for the Agent's control-plane connection (F10), tuned for the <b>WAN</b> case F35
/// makes first-class: a remote Agent reaches ZWarden.Web over the open Internet, where drops are longer and
/// more frequent than on a LAN.
/// </summary>
/// <remarks>
/// Two properties matter, and both differ from SignalR's built-in policy (which gives up after ~30s):
/// <list type="bullet">
/// <item>It <b>retries forever</b> — <see cref="NextRetryDelay"/> never returns <c>null</c> — so a remote
/// Agent that loses the control plane for hours reconnects on its own once the link returns, with no operator
/// action on the Host.</item>
/// <item>It backs off <b>exponentially, capped at 30s</b>, so a long outage settles into one attempt every
/// 30s rather than a busy-loop against the control plane.</item>
/// </list>
/// On each successful reconnect the connection re-sends <c>AgentHello</c> + <c>AgentStateSnapshot</c>
/// (<see cref="SignalRControlPlaneConnection"/>), so observed state is reconciled after the gap.
/// </remarks>
public sealed class CappedBackoffRetryPolicy : IRetryPolicy
{
    private const double CapSeconds = 30;

    /// <inheritdoc />
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        ArgumentNullException.ThrowIfNull(retryContext);

        // 2^count seconds, clamped to the cap. The inner Min bounds the exponent so Math.Pow cannot overflow
        // to Infinity on a very long outage; the outer Min applies the 30s ceiling.
        double seconds = Math.Min(CapSeconds, Math.Pow(2, Math.Min(retryContext.PreviousRetryCount, 5)));
        return TimeSpan.FromSeconds(seconds);
    }
}
