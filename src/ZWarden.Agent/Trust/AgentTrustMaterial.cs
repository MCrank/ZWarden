using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Agent.Trust;

/// <summary>
/// What an enrolled Agent holds after the exchange (F9; ADR 0007): its assigned <see cref="AgentId"/>, the
/// per-Agent <see cref="Credential"/> it presents to authenticate (F10's handshake), and the operator label.
/// The credential is a <see cref="SecretString"/>, so it never stringifies through logging, interpolation or
/// the debugger — the value is read only through <see cref="SecretString.Reveal"/> when it goes on the wire
/// or to the trust file.
/// </summary>
public sealed record AgentTrustMaterial(AgentId AgentId, SecretString Credential, string? Label);
