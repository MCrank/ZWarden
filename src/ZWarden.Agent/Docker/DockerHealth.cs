namespace ZWarden.Agent.Docker;

/// <summary>
/// A point-in-time diagnostic of the Agent's Docker connectivity (F13's "Docker health diagnostics"). It
/// reports whether the daemon is reachable and the API version negotiated over <c>/_ping</c> (ADR 0008: the
/// Agent negotiates, never pins a <c>/v1.xx</c> prefix). It deliberately does <b>not</b> report whether the
/// canonical PZ image is present: the ten-entry allowlist denies <c>/images/*</c> (ADR 0008 §3.3), so image
/// presence cannot be probed under the proxy — it surfaces instead as an actionable diagnostic at create time
/// (<see cref="ContainerCreateFailure.ImageNotProvisioned"/>).
/// </summary>
/// <param name="DaemonReachable">Whether the Docker daemon answered.</param>
/// <param name="ApiVersion">The negotiated Engine API version, or <c>null</c> when unreachable.</param>
/// <param name="Detail">An Agent-authored explanation when unhealthy; <c>null</c> when healthy.</param>
public sealed record DockerHealth(bool DaemonReachable, string? ApiVersion, string? Detail);
