using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZWarden.Web.Hosting;

namespace ZWarden.Web.Tests.Hosting;

/// <summary>
/// #322: SignalR logs why it closed a circuit connection only at Debug, under a category the default configuration
/// keeps at Warning, so an oversized hub message left nothing in the log. The close reason must reach the log at the
/// default level, and nothing else of SignalR's Debug output may.
/// </summary>
public sealed partial class HubCloseReasonLoggingTests
{
    private const string SizeLimit = "The maximum message size of 32768B was exceeded. The message size can be configured in AddHubOptions.";

    [Test]
    public async Task A_connection_closed_over_an_oversized_message_is_logged_as_a_warning_with_the_reason()
    {
        (ServiceProvider provider, CapturingProvider capture) = Build();
        await using (provider)
        {
            ILogger hub = provider.GetRequiredService<ILoggerFactory>().CreateLogger(HubCloseReasonLogging.HubConnectionHandlerCategory);

            InvalidDataException oversized = new(SizeLimit);
            ErrorProcessingRequest(hub, oversized);

            Entry warning = capture.Entries.Single(e => e.Level == LogLevel.Warning);
            await Assert.That(warning.Message).Contains(SizeLimit);
        }
    }

    [Test]
    public async Task Other_hub_errors_and_debug_chatter_stay_out_of_the_default_log()
    {
        (ServiceProvider provider, CapturingProvider capture) = Build();
        await using (provider)
        {
            ILogger hub = provider.GetRequiredService<ILoggerFactory>().CreateLogger(HubCloseReasonLogging.HubConnectionHandlerCategory);

            ConnectedStarting(hub);
            IOException gone = new("The client went away.");
            ErrorProcessingRequest(hub, gone);

            await Assert.That(capture.Entries).IsEmpty();
        }
    }

    // What SignalR's HubConnectionHandler logs (event ids and messages as it does).
    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Error when processing requests.")]
    private static partial void ErrorProcessingRequest(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug, Message = "OnConnectedAsync started.")]
    private static partial void ConnectedStarting(ILogger logger);

    // The host's default logging levels ("Microsoft.AspNetCore": Warning) with a capturing sink.
    private static (ServiceProvider Provider, CapturingProvider Capture) Build()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LogLevel:Default"] = "Information",
                ["LogLevel:Microsoft.AspNetCore"] = "Warning",
            })
            .Build();
        var capture = new CapturingProvider();
        ServiceProvider provider = new ServiceCollection()
            .AddLogging(logging => logging
                .AddConfiguration(configuration)
                .AddProvider(capture)
                .AddHubCloseReasonLogging())
            .BuildServiceProvider();
        return (provider, capture);
    }

    private sealed record Entry(string Category, LogLevel Level, string Message);

    private sealed class CapturingProvider : ILoggerProvider
    {
        private readonly List<Entry> _entries = [];

        public IReadOnlyList<Entry> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner._entries)
                {
                    owner._entries.Add(new Entry(category, logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
