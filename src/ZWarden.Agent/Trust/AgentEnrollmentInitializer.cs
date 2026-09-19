using System.Net.Sockets;
using System.Security.Authentication;
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
/// <remarks>
/// Enrolment-time <b>transport</b> failures are not fatal (#185). A TLS-trust failure (common in Private mode,
/// where the Agent must trust Caddy's internal CA), a socket/timeout, or an unreachable control plane must not
/// crash host startup — with <c>restart: unless-stopped</c> that becomes a stack-trace crash-loop for a
/// documented, expected misconfiguration. Instead the step logs a single actionable message and retries with
/// capped exponential backoff in the background, so once the operator fixes trust (or the control plane comes
/// up) the Agent enrols with no restart. A genuine refusal (the server's one generic failure) is not retried —
/// it means the one-time secret was consumed or invalid, which a retry cannot resolve.
/// </remarks>
public sealed partial class AgentEnrollmentInitializer : IHostedService, IDisposable
{
    private readonly IAgentTrustStore _store;
    private readonly IEnrollmentClient _client;
    private readonly AgentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentEnrollmentInitializer> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _retryTask;

    public AgentEnrollmentInitializer(
        IAgentTrustStore store,
        IEnrollmentClient client,
        IOptions<AgentOptions> options,
        TimeProvider timeProvider,
        ILogger<AgentEnrollmentInitializer> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _client = client;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private enum EnrollmentAttempt
    {
        /// <summary>The exchange succeeded and trust material was stored.</summary>
        Succeeded,

        /// <summary>The control plane answered with its one generic refusal — the secret is consumed/invalid; do not retry.</summary>
        Refused,

        /// <summary>The control-plane certificate is not trusted (TLS handshake failed) — retryable once trust is fixed.</summary>
        TransientTls,

        /// <summary>The control plane could not be reached (socket/timeout/transport error) — retryable once it is up.</summary>
        TransientUnreachable,
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

        // First attempt runs inline so a healthy control plane enrols before the connection step starts, keeping
        // the F8/F10 startup ordering. A transient/trust failure never throws out of here (that would crash the
        // host and, under restart: unless-stopped, crash-loop); it schedules a background retry instead.
        EnrollmentAttempt outcome = await TryEnrollAsync(cancellationToken).ConfigureAwait(false);
        switch (outcome)
        {
            case EnrollmentAttempt.Succeeded:
                return;
            case EnrollmentAttempt.Refused:
                LogEnrollmentFailed();
                return;
            case EnrollmentAttempt.TransientTls:
                LogTlsTrustFailure();
                break;
            case EnrollmentAttempt.TransientUnreachable:
            default:
                LogControlPlaneUnreachable();
                break;
        }

        _retryTask = Task.Run(() => RetryUntilEnrolledAsync(_stopping.Token), CancellationToken.None);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_retryTask is { } retry)
        {
            try
            {
                await retry.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: the background retry unwinds when cancelled on shutdown.
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _stopping.Dispose();

    // Runs one exchange, storing trust on success and classifying a transport failure as retryable. A genuine
    // refusal (null) is not retryable. Cancellation (shutdown) is never swallowed — it surfaces to the caller.
    private async Task<EnrollmentAttempt> TryEnrollAsync(CancellationToken cancellationToken)
    {
        try
        {
            AgentTrustMaterial? material = await _client
                .EnrollAsync(new SecretString(_options.EnrollmentSecret!), cancellationToken)
                .ConfigureAwait(false);
            if (material is null)
            {
                return EnrollmentAttempt.Refused;
            }

            await _store.SaveAsync(material, cancellationToken).ConfigureAwait(false);
            LogEnrolled(material.AgentId);
            return EnrollmentAttempt.Succeeded;
        }
        catch (Exception ex) when (IsTransientTransportFailure(ex, cancellationToken))
        {
            return IsTlsTrustFailure(ex) ? EnrollmentAttempt.TransientTls : EnrollmentAttempt.TransientUnreachable;
        }
    }

    // Retries with capped exponential backoff until the Agent enrols, the secret is refused, or shutdown. The
    // first actionable message is already logged by StartAsync; retries stay quiet so a long outage does not
    // spam the log (the exact opposite of the crash-loop stack traces this replaces — #185).
    private async Task RetryUntilEnrolledAsync(CancellationToken cancellationToken)
    {
        int attempt = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(BackoffDelay(attempt), _timeProvider, cancellationToken).ConfigureAwait(false);

                EnrollmentAttempt outcome = await TryEnrollAsync(cancellationToken).ConfigureAwait(false);
                if (outcome == EnrollmentAttempt.Succeeded)
                {
                    return;
                }

                if (outcome == EnrollmentAttempt.Refused)
                {
                    LogEnrollmentFailed();
                    return;
                }

                attempt++;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    // Exponential backoff from InitialDelay, doubling each attempt, clamped to MaxDelay. The exponent is bounded
    // so a long outage cannot overflow Math.Pow to Infinity before the cap applies.
    private TimeSpan BackoffDelay(int attempt)
    {
        double factor = Math.Pow(2, Math.Min(attempt, 16));
        double milliseconds = Math.Min(
            _options.EnrollmentRetryMaxDelay.TotalMilliseconds,
            _options.EnrollmentRetryInitialDelay.TotalMilliseconds * factor);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    // A transport failure we can retry: TLS trust, socket/DNS/reset, or an HTTP-level timeout. Never treat a
    // shutdown cancellation as a failure — let it propagate so the loop and host unwind cleanly.
    private static bool IsTransientTransportFailure(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return ex is HttpRequestException
            or AuthenticationException
            or SocketException
            or IOException
            or TimeoutException
            or TaskCanceledException;
    }

    // Distinguishes a TLS-trust failure (so the operator is pointed at the CA-trust docs) from a plain
    // reachability failure, by walking the exception chain for the tell-tale handshake errors.
    private static bool IsTlsTrustFailure(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException
                or HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError })
            {
                return true;
            }
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent already enrolled as {AgentId}; skipping enrollment.")]
    private partial void LogAlreadyEnrolled(AgentId agentId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Agent is not enrolled and no enrollment secret is configured; starting un-enrolled.")]
    private partial void LogNotEnrolled();

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent enrolled successfully as {AgentId}.")]
    private partial void LogEnrolled(AgentId agentId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Enrollment was refused by the control plane; the one-time secret is consumed or invalid, so it "
            + "will not be retried. Issue a new enrollment token and restart the Agent. Starting un-enrolled.")]
    private partial void LogEnrollmentFailed();

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Enrollment could not complete because the control-plane certificate is not trusted (TLS "
            + "handshake failed). In Private TLS mode the Agent must trust Caddy's internal CA — see the "
            + "\"Private mode: trusting Caddy's CA in the Agent\" section of docs/deployment/compose-reference.md. "
            + "The Agent will stay up and retry enrollment in the background; no restart is needed once trust is fixed.")]
    private partial void LogTlsTrustFailure();

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Enrollment could not reach the control plane (it may still be starting). The Agent will stay up "
            + "and retry enrollment in the background; no restart is needed once the control plane is reachable.")]
    private partial void LogControlPlaneUnreachable();
}
