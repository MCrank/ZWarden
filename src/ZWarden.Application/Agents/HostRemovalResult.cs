namespace ZWarden.Application.Agents;

/// <summary>
/// The outcome of removing a Host (#363): either it was removed, or it still has <see cref="ServerCount"/> Servers
/// and nothing changed (they must be deleted first, so no Server is left without its Agent).
/// </summary>
/// <param name="Removed">Whether the Agent record was deleted.</param>
/// <param name="ServerCount">The Servers still on the Host when removal was refused; 0 when removed.</param>
public sealed record HostRemovalResult(bool Removed, int ServerCount)
{
    /// <summary>The Host was removed.</summary>
    public static HostRemovalResult Done { get; } = new(true, 0);

    /// <summary>Refused: the Host still has <paramref name="serverCount"/> Servers.</summary>
    public static HostRemovalResult Blocked(int serverCount) => new(false, serverCount);
}
