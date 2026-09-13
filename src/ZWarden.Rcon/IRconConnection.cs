namespace ZWarden.Rcon;

/// <summary>
/// The behaviour of a single authenticated RCON connection, factored out of
/// <see cref="RconConnection"/> so the Agent can depend on the abstraction - injecting a fake in a
/// unit test without opening a real socket, and (later) pooling one live connection per server.
/// </summary>
public interface IRconConnection : IAsyncDisposable
{
    /// <summary>True once a socket is open and authenticated; cleared on any fault or disposal.</summary>
    bool IsConnected { get; }

    /// <summary>Opens the socket and authenticates now. Raises <see cref="RconAuthenticationException"/>
    /// on a rejected password, <see cref="RconTimeoutException"/> on a connect timeout, or
    /// <see cref="RconException"/> on any other transport failure (unreachable, RCON disabled, or the
    /// five-connection cap reached).</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends one admin command and returns its full response (empty string for an empty
    /// result, which is not a fault). The command text is sent as-is; quoting is the caller's job.</summary>
    Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates an <see cref="IRconConnection"/> for a resolved endpoint. A seam so the health probe can be
/// unit-tested with a fake connection, and so the connection's timing options are configured in one place.
/// </summary>
public interface IRconConnectionFactory
{
    /// <summary>Creates - but does not yet open - a connection to <paramref name="endpoint"/>.</summary>
    IRconConnection Create(RconEndpoint endpoint);
}

/// <summary>The production factory: builds a real <see cref="RconConnection"/> with the configured options.</summary>
public sealed class RconConnectionFactory : IRconConnectionFactory
{
    private readonly RconOptions _options;

    public RconConnectionFactory(RconOptions? options = null) => _options = options ?? RconOptions.Default;

    public IRconConnection Create(RconEndpoint endpoint) => new RconConnection(endpoint, _options);
}
