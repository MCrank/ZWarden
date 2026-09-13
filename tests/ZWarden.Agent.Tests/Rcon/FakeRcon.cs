using ZWarden.Agent.Rcon;
using ZWarden.Domain.Ids;
using ZWarden.Rcon;

namespace ZWarden.Agent.Tests.Rcon;

/// <summary>A fake <see cref="IRconConnection"/>: <see cref="ConnectAsync"/> throws
/// <see cref="ConnectException"/> when set, otherwise marks the connection open. Records connect and dispose so
/// a test can prove the probe always closes the socket (cap-slot safety).</summary>
internal sealed class FakeRconConnection : IRconConnection
{
    public Exception? ConnectException { get; set; }

    public int ConnectCount { get; private set; }

    public int DisposeCount { get; private set; }

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ConnectCount++;
        if (ConnectException is not null)
        {
            return Task.FromException(ConnectException);
        }

        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default) =>
        Task.FromResult(string.Empty);

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}

/// <summary>A fake factory that hands out a preset <see cref="FakeRconConnection"/> and records the endpoint.</summary>
internal sealed class FakeRconConnectionFactory : IRconConnectionFactory
{
    public FakeRconConnectionFactory(FakeRconConnection connection) => Connection = connection;

    public FakeRconConnection Connection { get; }

    public RconEndpoint? LastEndpoint { get; private set; }

    public IRconConnection Create(RconEndpoint endpoint)
    {
        LastEndpoint = endpoint;
        return Connection;
    }
}

/// <summary>A fake resolver that returns a preset <see cref="RconResolveResult"/> and records the Server.</summary>
internal sealed class FakeRconEndpointResolver : IRconEndpointResolver
{
    public RconResolveResult Result { get; set; } = RconResolveResult.NoContainer;

    public ServerId? LastServerId { get; private set; }

    public Task<RconResolveResult> ResolveAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        LastServerId = serverId;
        return Task.FromResult(Result);
    }
}
