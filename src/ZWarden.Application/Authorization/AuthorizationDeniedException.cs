namespace ZWarden.Application.Authorization;

/// <summary>
/// Thrown when a principal attempts a role-administration operation they are not authorized for (ADR 0018):
/// acting without <c>Role.Manage</c>, or trying to grant a permission the actor does not itself hold (no
/// self-escalation). The <see cref="System.Exception.Message"/> states the reason without echoing secrets.
/// </summary>
public sealed class AuthorizationDeniedException : Exception
{
    public AuthorizationDeniedException(string message)
        : base(message)
    {
    }

    public AuthorizationDeniedException()
    {
    }

    public AuthorizationDeniedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
