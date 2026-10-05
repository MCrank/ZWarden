namespace ZWarden.Web.Components.Hosts;

/// <summary>
/// The lines an operator pastes into the Agent's compose <c>.env</c> after minting an enrollment token (#342): the
/// control plane's domain (the Agent dials <c>wss://&lt;domain&gt;/agent/hub</c>) and the one-time secret. The domain
/// is the site the operator is using, so it's right for a remote Agent; the co-located Agent ignores it.
/// </summary>
public static class EnrollmentSnippet
{
    /// <summary>The <c>ZWARDEN_DOMAIN</c> value for <paramref name="baseUri"/>: its host, plus the port when it isn't
    /// the scheme's default.</summary>
    public static string Domain(Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        return baseUri.IsDefaultPort ? baseUri.Host : baseUri.Authority;
    }

    /// <summary>The two <c>.env</c> lines, one per line.</summary>
    public static string EnvLines(Uri baseUri, string secret)
        => $"ZWARDEN_DOMAIN={Domain(baseUri)}\nZWARDEN_ENROLLMENT_SECRET={secret}";
}
