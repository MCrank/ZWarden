using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Docker;

/// <summary>
/// The health-relevant facts read from a single container's Docker <c>inspect</c> (F16): its state, its image
/// HEALTHCHECK verdict, the last exit code and OOM flag, and the published ports. It is the raw material the
/// <c>ServerHealthObserver</c> turns into a health rollup; it carries no ZWarden interpretation.
/// </summary>
/// <param name="State">Docker's <c>State.Status</c>, e.g. <c>running</c>/<c>exited</c>/<c>dead</c>.</param>
/// <param name="HealthStatus">The <c>State.Health.Status</c> verdict (<c>healthy</c>/<c>unhealthy</c>/<c>starting</c>),
/// or <c>null</c> when the image declares no healthcheck.</param>
/// <param name="ExitCode">The last exit code (0 unless the container exited abnormally).</param>
/// <param name="OomKilled">Whether the container was OOM-killed.</param>
/// <param name="Ports">The container's published host↔container port mappings.</param>
public sealed record ContainerHealthFacts(
    string State,
    string? HealthStatus,
    long ExitCode,
    bool OomKilled,
    IReadOnlyList<PublishedPort> Ports);

/// <summary>One owned container the Agent inspected for health (F16): the Server it hosts and its inspected facts.</summary>
/// <param name="ServerId">The Server this container hosts.</param>
/// <param name="Facts">The health-relevant inspect facts.</param>
public sealed record ObservedContainer(ServerId ServerId, ContainerHealthFacts Facts);
