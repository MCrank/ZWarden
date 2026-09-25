namespace ZWarden.Domain.Servers;

/// <summary>
/// The rules for an operator-chosen host game port (#229), shared by the Web edge and the Agent. A Server publishes
/// a UDP <b>pair</b> on its host: the game port <c>p</c> (container 16261) and the direct port <c>p + 1</c>
/// (container 16262). Only the host side is chosen — the container-internal ports never change, so the server's
/// config file never needs editing (#228). A game port is acceptable when both ports of its pair are unprivileged
/// and addressable; two pairs clash when they share either port.
/// </summary>
public static class HostPortRules
{
    /// <summary>The lowest game port an operator may choose — the first unprivileged port.</summary>
    public const int MinGamePort = 1024;

    /// <summary>The highest game port an operator may choose, leaving room for its direct port above it.</summary>
    public const int MaxGamePort = 65534;

    /// <summary>
    /// Validates a requested host game port. Returns <c>null</c> when acceptable, or a short operator-facing reason
    /// why it was rejected.
    /// </summary>
    public static string? ValidateGamePort(int gamePort) =>
        gamePort is < MinGamePort or > MaxGamePort
            ? $"The game port must be between {MinGamePort} and {MaxGamePort} (the port above it is used too)."
            : null;

    /// <summary>The direct port paired with <paramref name="gamePort"/>: the port immediately above it.</summary>
    public static int DirectPortFor(int gamePort) => gamePort + 1;

    /// <summary>Whether the pairs starting at two game ports share a port.</summary>
    public static bool PairsOverlap(int gamePortA, int gamePortB) => Math.Abs(gamePortA - gamePortB) < 2;
}
