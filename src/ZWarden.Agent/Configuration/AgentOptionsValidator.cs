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

        if (options.HeartbeatInterval <= TimeSpan.Zero)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.HeartbeatInterval)} must be positive.");
        }

        if (options.HealthReportInterval <= TimeSpan.Zero)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.HealthReportInterval)} must be positive.");
        }

        if (options.ShutdownTimeout <= TimeSpan.Zero)
        {
            failures.Add($"{AgentOptions.SectionName}:{nameof(AgentOptions.ShutdownTimeout)} must be positive.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
