namespace ZWarden.Infrastructure.Security;

/// <summary>
/// Thrown when secret-key configuration cannot yield a valid, active key ring (ADR 0015). The loader
/// fails closed: a deployment missing or misconfiguring its keys does not start. Messages name the
/// offending key id and the problem, but never contain key bytes.
/// </summary>
public sealed class KeyRingConfigurationException : Exception
{
    public KeyRingConfigurationException()
    {
    }

    public KeyRingConfigurationException(string message)
        : base(message)
    {
    }

    public KeyRingConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
