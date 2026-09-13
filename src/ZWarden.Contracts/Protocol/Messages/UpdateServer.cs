namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Update (install/validate) a registered Server's Project Zomboid install via anonymous SteamCMD (F17). The
/// target Server is the envelope's <see cref="Envelope{TPayload}.ServerId"/>, so — like <see cref="StartServer"/>
/// — this command carries <b>no payload</b>. It is a <b>mutating, server-scoped</b> Operation, so it claims the
/// per-server lock (ADR 0022) for the whole SteamCMD run. The Agent cannot <c>exec</c> into the container
/// (ADR 0008 denies exec/attach): it requests the update by dropping a control-file into the writable data
/// volume and restarting, then observes the container's <c>logs</c> — reporting <see cref="OperationProgress"/>
/// as SteamCMD downloads and a terminal <see cref="OperationCompleted"/> (with the installed build id in its
/// <see cref="OperationCompleted.Update"/> result) decided by <b>parsing stdout</b>, since SteamCMD's exit codes
/// are undocumented by Valve (ADR 0009). Install, update and validate are the same SteamCMD verb; a repair is
/// this same command run again. It carries no free-form command.
/// </summary>
[ProtocolMessage("lifecycle.update-server")]
public sealed record UpdateServer : AgentCommand;
