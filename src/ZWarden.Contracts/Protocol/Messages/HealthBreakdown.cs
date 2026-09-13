namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// The hierarchical breakdown behind a <see cref="ServerHealth"/> rollup (F16): the four probe verdicts the
/// Agent gathered — container, process, startup, network — so the operator sees <i>why</i> a Server is degraded
/// or failed, not just that it is. It is a payload type carried by <see cref="HealthChanged"/> (like
/// <see cref="ProvisionResult"/> on <see cref="OperationCompleted"/>), not a message in its own right. Every
/// detail string is <b>untrusted</b> Agent output (trust-boundaries.md §8) — non-secret, rendered escaped,
/// stored length-bounded.
/// </summary>
/// <param name="Container">Is the container running? (Docker inspect <c>State.Status</c>.)</param>
/// <param name="Process">Is the game process alive and past its own HEALTHCHECK? (inspect <c>State.Health</c>.)</param>
/// <param name="Startup">Is the Server inside or past its startup window? (the image's <c>--start-period</c>.)</param>
/// <param name="Network">Are the published game/query UDP ports reachable on the host?</param>
public sealed record HealthBreakdown(
    ProbeCheck Container,
    ProbeCheck Process,
    ProbeCheck Startup,
    ProbeCheck Network);

/// <summary>One probe's verdict within a <see cref="HealthBreakdown"/> (F16).</summary>
/// <param name="Status">The probe's outcome.</param>
/// <param name="Detail">An optional, <b>untrusted</b> human-readable note (trust-boundaries.md §8); may be null.</param>
public sealed record ProbeCheck(ProbeStatus Status, string? Detail = null);
