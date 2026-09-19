namespace ZWarden.Agent.Health;

/// <summary>
/// The Agent's network probe (F16): can a Server's game UDP port be reached at a given address? UDP is
/// connectionless, so this is deliberately <b>best-effort</b> — it returns <c>false</c> only on a definitive
/// "port closed" signal and <c>null</c> (unknown) on any uncertainty, so a probe that cannot tell never
/// downgrades a Server's health. The rollup treats <c>null</c> as "not probed". The caller supplies the address
/// to probe, because the reachable address depends on the Agent's deployment topology: when the Agent runs in its
/// own container (the reference compose distribution) the published host port is not on the Agent's loopback, so
/// the caller passes the PZ container's address on the shared ZWarden network instead (#199).
/// </summary>
public interface INetworkReachabilityProbe
{
    /// <summary>Probes a UDP <paramref name="port"/> at <paramref name="host"/>. <c>true</c> = reachable,
    /// <c>false</c> = definitively closed, <c>null</c> = could not determine (the safe default — does not degrade
    /// health; also returned for a null/empty/unparseable host).</summary>
    Task<bool?> IsUdpPortReachableAsync(string host, int port, CancellationToken cancellationToken);
}
