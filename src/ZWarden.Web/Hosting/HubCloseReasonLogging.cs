using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ZWarden.Web.Hosting;

/// <summary>
/// #322: makes a Blazor circuit connection that SignalR closes over a protocol violation show up in the log at the
/// default level. SignalR logs the reason (for example "The maximum message size of 32768B was exceeded") only at
/// Debug, under a category the default configuration keeps at Warning, so an operator saw the reconnect overlay and an
/// empty log. This repeats that one case as a Warning, without turning the rest of SignalR's Debug chatter on.
/// </summary>
public static class HubCloseReasonLogging
{
    /// <summary>The category SignalR's hub connection handler logs under.</summary>
    public const string HubConnectionHandlerCategory = "Microsoft.AspNetCore.SignalR.HubConnectionHandler";

    /// <summary>Adds the provider, and lets only it see the hub connection handler's Debug messages.</summary>
    public static ILoggingBuilder AddHubCloseReasonLogging(this ILoggingBuilder logging)
    {
        ArgumentNullException.ThrowIfNull(logging);
        logging.Services.AddSingleton<ILoggerProvider, HubCloseReasonLoggerProvider>();
        logging.AddFilter<HubCloseReasonLoggerProvider>(HubConnectionHandlerCategory, LogLevel.Debug);
        return logging;
    }
}

/// <summary>
/// Watches SignalR's hub connection handler and re-logs, as a Warning under its own category, a connection closed
/// because the client sent data the hub refused (<see cref="InvalidDataException"/>: an oversized or malformed
/// message). Other errors there (a client simply going away) stay at Debug.
/// </summary>
public sealed partial class HubCloseReasonLoggerProvider(IServiceProvider services) : ILoggerProvider
{
    // Resolved on first use: the logger factory that builds this provider can't be resolved while building it.
    private ILogger? _warnings;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) =>
        categoryName.StartsWith(HubCloseReasonLogging.HubConnectionHandlerCategory, StringComparison.Ordinal)
            ? new Watcher(this)
            : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private void Report(Exception exception)
    {
        _warnings ??= services.GetRequiredService<ILogger<HubCloseReasonLoggerProvider>>();
        LogConnectionClosed(_warnings, exception.Message, exception);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "A Blazor circuit connection was closed because the browser sent data the hub refused: {Reason}")]
    private static partial void LogConnectionClosed(ILogger logger, string reason, Exception exception);

    private sealed class Watcher(HubCloseReasonLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is InvalidDataException)
            {
                provider.Report(exception);
            }
        }
    }
}
