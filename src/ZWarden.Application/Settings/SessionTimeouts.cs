namespace ZWarden.Application.Settings;

/// <summary>Human wording for a session idle timeout (#346): "30 minutes", "1 hour", "24 hours", "7 days".</summary>
public static class SessionTimeouts
{
    public static string Describe(TimeSpan timeout)
    {
        double minutes = timeout.TotalMinutes;
        if (minutes >= 2 * 24 * 60 && minutes % (24 * 60) == 0)
        {
            return $"{(int)timeout.TotalDays} days";
        }

        if (minutes >= 60 && minutes % 60 == 0)
        {
            return timeout.TotalHours == 1 ? "1 hour" : $"{(int)timeout.TotalHours} hours";
        }

        return minutes == 1 ? "1 minute" : $"{(int)minutes} minutes";
    }
}
