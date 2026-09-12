namespace ZWarden.Infrastructure.Agents;

/// <summary>The prefixes on generated bearer secrets — human- and secret-scanner-recognisable (decision 2).</summary>
internal static class CredentialPrefixes
{
    /// <summary>The one-time enrollment secret.</summary>
    public const string Enrollment = "zwe";

    /// <summary>The per-Agent credential.</summary>
    public const string Agent = "zwa";
}
