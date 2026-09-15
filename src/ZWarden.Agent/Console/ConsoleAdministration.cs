using ZWarden.Agent.Rcon;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Console;
using ZWarden.Domain.Ids;
using ZWarden.Rcon;

namespace ZWarden.Agent.Console;

/// <summary>
/// Runs a remote-console command for one Server over the Agent-owned RCON connection (F28): re-check the input
/// against <see cref="ConsoleCommandRules"/> (defence in depth — the Web edge already checked it, but the command
/// is actually sent here, trust-boundaries.md §8), resolve the Server's private endpoint, send the line as-is
/// (ADR 0026 — quoting is the operator's job), and return the untrusted reply bounded to
/// <see cref="MaxOutputLength"/>. Any reason it could not run — a policy/validation refusal, RCON disabled or
/// unreachable, the password rejected, a timeout — is raised as a <see cref="ConsoleCommandException"/> with an
/// Agent-authored, non-secret message; the command processor maps that to a failed Operation. The connection is
/// always disposed (cap-slot safety, research §7 quirk 7).
/// </summary>
public interface IConsoleAdministration
{
    Task<ConsoleCommandResult> ExecuteAsync(ServerId serverId, string input, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class ConsoleAdministration : IConsoleAdministration
{
    /// <summary>The greatest reply length the Agent carries back. A large reply (<c>help</c>, <c>showoptions</c>)
    /// can be tens of KB; beyond this the reply is cut and reported as <see cref="ConsoleCommandResult.Truncated"/>
    /// so the untrusted payload cannot grow without bound (trust-boundaries.md §8).</summary>
    public const int MaxOutputLength = 64 * 1024;

    private readonly IRconEndpointResolver _resolver;
    private readonly IRconConnectionFactory _connections;

    public ConsoleAdministration(IRconEndpointResolver resolver, IRconConnectionFactory connections)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(connections);
        _resolver = resolver;
        _connections = connections;
    }

    /// <inheritdoc />
    public async Task<ConsoleCommandResult> ExecuteAsync(ServerId serverId, string input, CancellationToken cancellationToken)
    {
        // Defence in depth: the Web edge validated and policy-checked this line before enqueue, but this is where
        // it is actually assembled and sent, so re-run both checks (trust-boundaries.md §8, F28 D-1 / ADR 0032).
        if (ConsoleCommandRules.ValidateInput(input) is { } invalid)
        {
            throw new ConsoleCommandException(invalid);
        }

        if (ConsoleCommandRules.EvaluatePolicy(input) is { } denied)
        {
            throw new ConsoleCommandException(denied);
        }

        RconResolveResult resolution = await _resolver.ResolveAsync(serverId, cancellationToken).ConfigureAwait(false);
        switch (resolution.Status)
        {
            case RconResolveStatus.NoContainer:
                throw new ConsoleCommandException(
                    "No running container on this host for the server, or it has no address on the ZWarden network.");

            case RconResolveStatus.RconDisabled:
                throw new ConsoleCommandException(
                    "RCON is disabled: no password is set in the server configuration.");
        }

        RconEndpoint endpoint = resolution.Endpoint!.Value;
        IRconConnection connection = _connections.Create(endpoint);
        try
        {
            string reply = await connection.ExecuteAsync(input, cancellationToken).ConfigureAwait(false);
            return reply.Length > MaxOutputLength
                ? new ConsoleCommandResult(reply[..MaxOutputLength], Truncated: true)
                : new ConsoleCommandResult(reply, Truncated: false);
        }
        catch (RconAuthenticationException)
        {
            throw new ConsoleCommandException("The RCON password was rejected by the server.");
        }
        catch (RconTimeoutException)
        {
            throw new ConsoleCommandException("Timed out running the command against the server's RCON port.");
        }
        catch (RconException)
        {
            throw new ConsoleCommandException(
                "The server's RCON port could not be reached (refused, or the connection cap is full).");
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
