using System.Globalization;
using ZWarden.Domain.Servers;

namespace ZWarden.Web.Components.Servers;

/// <summary>
/// Parses the optional host game port an operator types into a form (#229): blank means "no choice" (the Agent's next
/// free stride on register, the current pair on recreate); anything else must be a whole number accepted by
/// <see cref="HostPortRules"/>. Shared by the register forms and the server-detail recreate form so they agree.
/// </summary>
public static class HostPortInput
{
    /// <summary>Parses <paramref name="text"/>. Returns <c>false</c> with an operator-facing <paramref name="error"/>
    /// when it is not blank and not an acceptable game port; otherwise <c>true</c>, with <paramref name="gamePort"/>
    /// <c>null</c> for blank.</summary>
    public static bool TryParse(string? text, out int? gamePort, out string? error)
    {
        gamePort = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port))
        {
            error = "The game port must be a whole number.";
            return false;
        }

        error = HostPortRules.ValidateGamePort(port);
        if (error is not null)
        {
            return false;
        }

        gamePort = port;
        return true;
    }
}
