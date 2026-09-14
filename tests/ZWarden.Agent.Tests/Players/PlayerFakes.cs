using ZWarden.Agent.Players;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.Rcon;

namespace ZWarden.Agent.Tests.Players;

/// <summary>A fake <see cref="IRconConnection"/> that returns a scripted reply (or throws a scripted RCON
/// exception) from <see cref="ExecuteAsync"/>, records the command it was asked to run, and counts disposals so a
/// test can prove the socket is always closed (cap-slot safety).</summary>
internal sealed class ScriptedRconConnection : IRconConnection
{
    private readonly string _reply;
    private readonly Exception? _executeThrow;

    public ScriptedRconConnection(string reply = "", Exception? executeThrow = null)
    {
        _reply = reply;
        _executeThrow = executeThrow;
    }

    public string? LastCommand { get; private set; }

    public int DisposeCount { get; private set; }

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
        LastCommand = command;
        return _executeThrow is not null ? Task.FromException<string>(_executeThrow) : Task.FromResult(_reply);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}

/// <summary>A fake factory that hands out a preset <see cref="ScriptedRconConnection"/> and records the endpoint
/// and how many connections it created.</summary>
internal sealed class ScriptedRconConnectionFactory : IRconConnectionFactory
{
    public ScriptedRconConnectionFactory(ScriptedRconConnection connection) => Connection = connection;

    public ScriptedRconConnection Connection { get; }

    public int CreateCount { get; private set; }

    public RconEndpoint? LastEndpoint { get; private set; }

    public IRconConnection Create(RconEndpoint endpoint)
    {
        CreateCount++;
        LastEndpoint = endpoint;
        return Connection;
    }
}

/// <summary>A fake <see cref="IPlayerAdministration"/> for the command-processor tests: returns preset results (or
/// throws a preset exception) and records the last call's arguments.</summary>
internal sealed class FakePlayerAdministration : IPlayerAdministration
{
    public PlayerRosterResult Roster { get; set; } = new(0, []);

    public PlayerActionResult ActionResult { get; set; } = new(PlayerActionOutcome.Applied, null);

    public Exception? Throw { get; set; }

    public int CallCount { get; private set; }

    public ServerId? LastServerId { get; private set; }

    public string? LastUsername { get; private set; }

    public string? LastReason { get; private set; }

    public bool? LastOpen { get; private set; }

    public Task<PlayerRosterResult> ListPlayersAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        Record(serverId);
        return Throw is not null ? Task.FromException<PlayerRosterResult>(Throw) : Task.FromResult(Roster);
    }

    public Task<PlayerActionResult> KickAsync(ServerId serverId, string username, string? reason, CancellationToken cancellationToken)
    {
        Record(serverId, username, reason);
        return Action();
    }

    public Task<PlayerActionResult> BanAsync(ServerId serverId, string username, string? reason, CancellationToken cancellationToken)
    {
        Record(serverId, username, reason);
        return Action();
    }

    public Task<PlayerActionResult> UnbanAsync(ServerId serverId, string username, CancellationToken cancellationToken)
    {
        Record(serverId, username);
        return Action();
    }

    public Task<PlayerActionResult> RemoveFromWhitelistAsync(ServerId serverId, string username, CancellationToken cancellationToken)
    {
        Record(serverId, username);
        return Action();
    }

    public Task<PlayerActionResult> SetWhitelistModeAsync(ServerId serverId, bool open, CancellationToken cancellationToken)
    {
        Record(serverId);
        LastOpen = open;
        return Action();
    }

    private Task<PlayerActionResult> Action() =>
        Throw is not null ? Task.FromException<PlayerActionResult>(Throw) : Task.FromResult(ActionResult);

    private void Record(ServerId serverId, string? username = null, string? reason = null)
    {
        CallCount++;
        LastServerId = serverId;
        LastUsername = username;
        LastReason = reason;
    }
}
