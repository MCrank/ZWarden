namespace ZWarden.Agent.Docker;

/// <summary>
/// Whether the Agent's configured canonical PZ image (<see cref="Configuration.AgentOptions.PzImageReference"/>) can
/// provision a server (#364): it must be set, and pinned rather than a floating <c>latest</c> (ADR 0008 invariant 9).
/// The provisioner refuses with <see cref="Problem"/> before it changes anything, and the Agent reports the result on
/// hello so the control plane can refuse the Host up front; <see cref="PzContainerFactory"/> keeps the floating-tag
/// guard as the last line of defense.
/// </summary>
public static class PzImageRules
{
    /// <summary>The actionable reason <paramref name="reference"/> can't provision a server, or <c>null</c> when it can.</summary>
    public static string? Problem(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return "This host has no Project Zomboid image configured. Set ZWARDEN_PZ_IMAGE in the Agent's .env and restart the Agent.";
        }

        return IsFloating(reference)
            ? $"This host's Project Zomboid image '{reference}' is a floating 'latest' tag, which is refused (ADR 0008). "
                + "Set ZWARDEN_PZ_IMAGE in the Agent's .env to a specific tag or digest and restart the Agent."
            : null;
    }

    /// <summary>Whether <paramref name="reference"/> is the floating <c>latest</c> tag.</summary>
    public static bool IsFloating(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference.Equals("latest", StringComparison.Ordinal)
            || reference.EndsWith(":latest", StringComparison.Ordinal);
    }
}
