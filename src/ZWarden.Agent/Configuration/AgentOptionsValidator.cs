using Microsoft.Extensions.Options;

namespace ZWarden.Agent.Configuration;

/// <summary>
/// Fail-closed startup validation for <see cref="AgentOptions"/> (F8). Every rejection names the
/// offending option and the rule it broke, so a misconfiguration is actionable at startup rather
/// than a runtime surprise.
/// </summary>
public sealed class AgentOptionsValidator : IValidateOptions<AgentOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.IdentityFilePath))
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.IdentityFilePath)} must be a non-empty path.");
        }

        if (string.IsNullOrWhiteSpace(options.ControlPlaneUri)
            || !Uri.TryCreate(options.ControlPlaneUri, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != "wss"))
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.ControlPlaneUri)} must be an absolute https or wss URI.");
        }

        if (!string.IsNullOrWhiteSpace(options.DockerEndpoint)
            && !Uri.TryCreate(options.DockerEndpoint, UriKind.Absolute, out _))
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.DockerEndpoint)} must be an absolute URI when set (e.g. unix:///var/run/docker.sock).");
        }

        if (string.IsNullOrWhiteSpace(options.NetworkName))
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.NetworkName)} must be a non-empty network name.");
        }

        if (string.IsNullOrWhiteSpace(options.DataMountRoot) || !Path.IsPathFullyQualified(options.DataMountRoot))
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.DataMountRoot)} must be an absolute host path.");
        }

        if (string.IsNullOrWhiteSpace(options.BackupRoot) || !Path.IsPathFullyQualified(options.BackupRoot))
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.BackupRoot)} must be an absolute host path.");
        }

        if (options.DefaultHeapSizeBytes <= 0)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.DefaultHeapSizeBytes)} must be positive.");
        }

        if (options.MemoryOverheadBytes < 0)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.MemoryOverheadBytes)} must not be negative.");
        }

        // Headroom invariant (#198): a limit equal to (or below) the JVM heap OOM-kills the container on boot,
        // because ZGC/native/metaspace and PZ's off-heap world load run well over the heap. Fail closed at startup.
        if (options.DefaultMemoryLimitBytes <= options.DefaultHeapSizeBytes)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.DefaultMemoryLimitBytes)} must exceed {nameof(AgentOptions.DefaultHeapSizeBytes)} to leave headroom for non-heap memory.");
        }

        if (options.StopTimeoutSeconds <= 0)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.StopTimeoutSeconds)} must be a positive number of seconds.");
        }

        if (Domain.Servers.GracefulRestartRules.ValidateSchedule(options.RestartWarningLeadSeconds) is { } scheduleError)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.RestartWarningLeadSeconds)} {scheduleError}");
        }

        if (Domain.Servers.GracefulRestartRules.ValidateReason(options.RestartWarningReason) is { } reasonError)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.RestartWarningReason)} {reasonError}");
        }

        if (options.EnrollmentRetryInitialDelay <= TimeSpan.Zero)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.EnrollmentRetryInitialDelay)} must be positive.");
        }

        if (options.EnrollmentRetryMaxDelay < options.EnrollmentRetryInitialDelay)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.EnrollmentRetryMaxDelay)} must be positive and no smaller than {nameof(AgentOptions.EnrollmentRetryInitialDelay)}.");
        }

        if (options.HeartbeatInterval <= TimeSpan.Zero)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.HeartbeatInterval)} must be positive.");
        }

        if (options.HealthReportInterval <= TimeSpan.Zero)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.HealthReportInterval)} must be positive.");
        }

        if (options.MetricsReportInterval <= TimeSpan.Zero)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.MetricsReportInterval)} must be positive.");
        }

        if (options.PlayerCountSampleInterval < AgentOptions.MinimumPlayerCountSampleInterval)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.PlayerCountSampleInterval)} must be at least {AgentOptions.MinimumPlayerCountSampleInterval.TotalSeconds:0} seconds.");
        }

        if (options.ShutdownTimeout <= TimeSpan.Zero)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.ShutdownTimeout)} must be positive.");
        }

        if (options.LogTailLines <= 0)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.LogTailLines)} must be positive.");
        }

        if (options.LogLineMaxCharacters <= 0)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.LogLineMaxCharacters)} must be positive.");
        }

        if (options.LogMaxLinesPerSecond <= 0)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.LogMaxLinesPerSecond)} must be positive.");
        }

        if (options.LogBatchFlushInterval <= TimeSpan.Zero)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.LogBatchFlushInterval)} must be positive.");
        }

        if (options.LogFollowRetryInterval <= TimeSpan.Zero)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.LogFollowRetryInterval)} must be positive.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
