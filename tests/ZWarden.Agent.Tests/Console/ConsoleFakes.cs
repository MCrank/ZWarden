using ZWarden.Agent.Console;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Console;

/// <summary>A fake <see cref="IConsoleAdministration"/> for the command-processor tests: returns a preset result
/// (or throws a preset exception) and records the last call's arguments.</summary>
internal sealed class FakeConsoleAdministration : IConsoleAdministration
{
    public ConsoleCommandResult Result { get; set; } = new(string.Empty, Truncated: false);

    public Exception? Throw { get; set; }

    public int CallCount { get; private set; }

    public ServerId? LastServerId { get; private set; }

    public string? LastInput { get; private set; }

    public Task<ConsoleCommandResult> ExecuteAsync(ServerId serverId, string input, CancellationToken cancellationToken)
    {
        CallCount++;
        LastServerId = serverId;
        LastInput = input;
        return Throw is not null ? Task.FromException<ConsoleCommandResult>(Throw) : Task.FromResult(Result);
    }
}
