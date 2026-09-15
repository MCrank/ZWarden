namespace ZWarden.Application.Agents;

/// <summary>
/// The Host facts an Agent self-reports on connect (F35 D-1) — the Application-layer carrier the connection
/// path threads into <see cref="IAgentConnectionStateWriter.MarkConnectedAsync"/> so the persisted Agent
/// record can render a legible multi-Host inventory. It mirrors the wire <c>HostDescriptor</c> without the
/// Application layer taking a dependency on the Contracts assembly.
/// </summary>
/// <remarks>
/// <b>Observed, not trusted</b> (trust-boundaries.md §3): display-only, never an authorization input.
/// </remarks>
public sealed record HostFacts(string Hostname, string AgentVersion, string OsPlatform);
