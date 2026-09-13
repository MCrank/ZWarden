namespace ZWarden.Agent.Health;

/// <summary>
/// The Agent's host-side network probe (F16): can a Server's published UDP port be reached on the host? UDP is
/// connectionless, so this is deliberately <b>best-effort</b> — it returns <c>false</c> only on a definitive
/// "port closed" signal and <c>null</c> (unknown) on any uncertainty, so a probe that cannot tell never
/// downgrades a Server's health. The rollup treats <c>null</c> as "not probed".
/// </summary>
public interface INetworkReachabilityProbe
{
    /// <summary>Probes a UDP port on the host loopback. <c>true</c> = reachable, <c>false</c> = definitively
    /// closed, <c>null</c> = could not determine (the safe default — does not degrade health).</summary>
    Task<bool?> IsUdpPortReachableAsync(int port, CancellationToken cancellationToken);
}
