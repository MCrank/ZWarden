namespace ZWarden.Agent.ServerConfig;

/// <summary>
/// Surgical <c>key=value</c> line edits on <c>servertest.ini</c>, shared by the provision-time seeders (F18 RCON, #230
/// initial settings). A key matches on the text before its first <c>=</c> (trimmed, ordinal); setting a key replaces
/// its line in place or appends it, and every other line — including comments and keys PZ wrote — is kept verbatim.
/// Callers must only pass values that cannot break the line (no control characters).
/// </summary>
internal static class IniKeyLines
{
    /// <summary>The value of the first line for <paramref name="key"/>, or <c>null</c> when absent.</summary>
    public static string? FindValue(IReadOnlyList<string> lines, string key)
    {
        foreach (string line in lines)
        {
            if (TryMatchKey(line, key, out string value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Replaces the line for <paramref name="key"/> in place, or appends one.</summary>
    public static void SetKey(List<string> lines, string key, string value)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (TryMatchKey(lines[i], key, out _))
            {
                lines[i] = $"{key}={value}";
                return;
            }
        }

        lines.Add($"{key}={value}");
    }

    private static bool TryMatchKey(string line, string key, out string value)
    {
        value = string.Empty;
        int eq = line.IndexOf('=', StringComparison.Ordinal);
        if (eq < 0)
        {
            return false;
        }

        if (!line.AsSpan(0, eq).Trim().Equals(key, StringComparison.Ordinal))
        {
            return false;
        }

        value = line[(eq + 1)..];
        return true;
    }
}
