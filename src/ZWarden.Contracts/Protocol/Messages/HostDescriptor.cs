namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// The self-reported facts a ZWarden.Agent tells the control plane about the Host it runs on, so a
/// multi-Host fleet is legible in the operator inventory (F35 D-1). It travels on <see cref="AgentHello"/>.
/// </summary>
/// <remarks>
/// These are <b>observed, not trusted</b> (trust-boundaries.md §3): they are display-only and must never be
/// an authorization input — an Agent could report anything here, and trust rests on its credential (ADR 0007),
/// not on what it says its hostname is. The descriptor is optional on the wire so an older Agent that predates
/// F35 still connects (its <see cref="AgentHello.Host"/> is simply <c>null</c>).
/// </remarks>
/// <param name="Hostname">The Host's machine name (e.g. <c>Environment.MachineName</c>). Never a secret.</param>
/// <param name="AgentVersion">The ZWarden.Agent build version reporting in.</param>
/// <param name="OsPlatform">A short OS-platform label (e.g. <c>Linux</c>, <c>Windows</c>, <c>macOS</c>).</param>
public sealed record HostDescriptor(string Hostname, string AgentVersion, string OsPlatform);
