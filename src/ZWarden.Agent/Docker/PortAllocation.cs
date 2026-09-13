namespace ZWarden.Agent.Docker;

/// <summary>
/// The host UDP port pair a single Server's container publishes (PRD 28): the game/Steam port and the direct
/// port. RCON (27015/tcp) is deliberately <b>not</b> here — it is never host-published (F12/F18). One Server
/// consumes exactly one <see cref="PortAllocation"/>; multiple Servers on a host are spaced a two-port stride
/// apart (<see cref="PortStrideAllocator"/>).
/// </summary>
/// <param name="GamePort">The host UDP port mapped to the container's 16261/udp (game/Steam).</param>
/// <param name="DirectPort">The host UDP port mapped to the container's 16262/udp (direct).</param>
public readonly record struct PortAllocation(ushort GamePort, ushort DirectPort);
