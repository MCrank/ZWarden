using ZWarden.Domain.Security;

namespace ZWarden.Rcon;

/// <summary>
/// Where a single PZ server's RCON listener lives and the password to reach it. This is the "RCON
/// password type" that trust-boundaries §9 test 7 requires be unreachable from <c>ZWarden.Web</c>:
/// it exists only in this Agent-only assembly. The password is a <see cref="SecretString"/> so it
/// never stringifies through a log line or the debugger; it is read only at the moment of building
/// the auth packet.
/// </summary>
/// <param name="Host">The container's address on the private ZWarden network - a bridge IP resolved
/// from <c>docker inspect</c>, never a host-published port (RCON is never host-published).</param>
/// <param name="Port">The RCON TCP port inside the container. Always 27015 in ZWarden's canonical
/// container; carried explicitly so a test or a non-default server can override it.</param>
/// <param name="Password">The RCON password the Agent generated and owns. An empty password means
/// PZ has RCON disabled (research §7); the client treats a connect that immediately EOFs as exactly
/// that case rather than a transport fault.</param>
public readonly record struct RconEndpoint(string Host, int Port, SecretString Password)
{
    /// <summary>Never renders the password - only the reachable address, safe for logs.</summary>
    public override string ToString() => $"{Host}:{Port}";
}
