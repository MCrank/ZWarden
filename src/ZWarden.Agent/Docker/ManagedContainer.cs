using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Docker;

/// <summary>
/// A canonical ZWarden.PZServer container this Agent owns, as surfaced by discovery: the Docker id, the Server
/// it hosts (read from <c>io.zwarden.server-id</c>) and its state. Only containers that pass canonical-label
/// validation <b>and</b> ownership (<c>io.zwarden.agent-id</c> == this Agent) ever become a
/// <see cref="ManagedContainer"/>; everything else on the host is invisible to the Agent by construction.
/// </summary>
/// <param name="DockerId">The Docker container id.</param>
/// <param name="ServerId">The Server this container hosts.</param>
/// <param name="State">The container state string (e.g. <c>running</c>, <c>exited</c>).</param>
public sealed record ManagedContainer(string DockerId, ServerId ServerId, string State);
