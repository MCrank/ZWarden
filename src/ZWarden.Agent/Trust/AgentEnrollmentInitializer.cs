using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Agent.Trust;

/// <summary>
/// The startup step that enrols the Agent when needed (F9; ADR 0007). If the trust file already holds
/// material, it is a no-op (enrolment is one-shot). Otherwise, if an enrollment secret is configured, it runs
/// the exchange and stores the result; if no secret is configured, the Agent starts <b>un-enrolled</b> — the
/// host still runs (F10 owns connecting, which an un-enrolled Agent cannot yet do). A failed exchange writes
/// no trust file. A malformed existing trust file fails typed (from the store) rather than being overwritten.
/// </summary>
public sealed partial class AgentEnrollmentInitializer : IHostedService
{
    private readonly IAgentTrustStore _store;
    private readonly IEnrollmentClient _client;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentEnrollmentInitializer> _logger;

    public AgentEnrollmentInitializer(
        IAgentTrustStore store,
        IEnrollmentClient client,
        IOptions<AgentOptions> options,
        ILogger<AgentEnrollmentInitializer> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        AgentTrustMaterial? existing = await _store.TryLoadAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            LogAlreadyEnrolled(existing.AgentId);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.EnrollmentSecret))
        {
            LogNotEnrolled();
            return;
        }

        AgentTrustMaterial? material = await _client
            .EnrollAsync(new SecretString(_options.EnrollmentSecret), cancellationToken)
            .ConfigureAwait(false);
        if (material is null)
        {
            LogEnrollmentFailed();
            return;
        }

        await _store.SaveAsync(material, cancellationToken).ConfigureAwait(false);
        LogEnrolled(material.AgentId);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent already enrolled as {AgentId}; skipping enrollment.")]
    private partial void LogAlreadyEnrolled(AgentId agentId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Agent is not enrolled and no enrollment secret is configured; starting un-enrolled.")]
    private partial void LogNotEnrolled();

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent enrolled successfully as {AgentId}.")]
    private partial void LogEnrolled(AgentId agentId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Enrollment was configured but the exchange failed; the Agent starts un-enrolled.")]
    private partial void LogEnrollmentFailed();
}
