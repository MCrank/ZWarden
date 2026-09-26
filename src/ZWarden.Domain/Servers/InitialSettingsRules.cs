namespace ZWarden.Domain.Servers;

/// <summary>
/// The rules for the basic settings chosen in the new-server wizard (#230), shared by the Web edge and the Agent,
/// which seeds them as <c>key=value</c> lines into <c>servertest.ini</c> before the first boot. A value is written
/// verbatim after the first <c>=</c>, so it is safe only because a value containing a line break or any other control
/// character — which could end the line and inject another key — is rejected outright, never rewritten. Values are
/// limited to printable ASCII so the file's encoding can never change their meaning.
/// </summary>
public static class InitialSettingsRules
{
    /// <summary>The lowest <c>MaxPlayers</c> PZ accepts.</summary>
    public const int MinMaxPlayers = 1;

    /// <summary>The highest <c>MaxPlayers</c> PZ accepts.</summary>
    public const int MaxMaxPlayers = 254;

    /// <summary>The longest public name accepted.</summary>
    public const int MaxPublicNameLength = 64;

    /// <summary>The longest join password accepted.</summary>
    public const int MaxPasswordLength = 64;

    /// <summary>The longest welcome message accepted.</summary>
    public const int MaxWelcomeMessageLength = 500;

    /// <summary>Validates a <c>MaxPlayers</c> value; <c>null</c> when acceptable, else the reason.</summary>
    public static string? ValidateMaxPlayers(int maxPlayers) =>
        maxPlayers is < MinMaxPlayers or > MaxMaxPlayers
            ? $"Max players must be between {MinMaxPlayers} and {MaxMaxPlayers}."
            : null;

    /// <summary>Validates a public name; <c>null</c> when acceptable (or absent), else the reason.</summary>
    public static string? ValidatePublicName(string? value) => ValidateText("The public name", value, MaxPublicNameLength);

    /// <summary>Validates a join password; <c>null</c> when acceptable (or absent), else the reason. The reason never
    /// echoes the value.</summary>
    public static string? ValidatePassword(string? value) => ValidateText("The password", value, MaxPasswordLength);

    /// <summary>Validates a welcome message; <c>null</c> when acceptable (or absent), else the reason.</summary>
    public static string? ValidateWelcomeMessage(string? value) =>
        ValidateText("The welcome message", value, MaxWelcomeMessageLength);

    private static string? ValidateText(string what, string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length > maxLength)
        {
            return $"{what} must be {maxLength} characters or fewer.";
        }

        foreach (char c in value)
        {
            if (c is < ' ' or > '~')
            {
                return $"{what} must contain only printable ASCII characters (no line breaks).";
            }
        }

        return null;
    }
}
