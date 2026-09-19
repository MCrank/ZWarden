namespace ZWarden.Application.Workshop;

/// <summary>
/// Pure parsing of the operator-pasted Workshop reference the Mod Browser accepts (#110 PR-C): a bare numeric
/// Workshop item/collection id, or a Steam URL that carries one as its <c>id</c> query parameter (e.g.
/// <c>https://steamcommunity.com/sharedfiles/filedetails/?id=2392709985</c> or a
/// <c>.../workshop/filedetails/?id=</c> collection link). The input is <b>untrusted</b> (trust-boundaries.md §3):
/// this only extracts a bounded numeric id and never dereferences the URL — the id is what reaches the keyless
/// metadata client, which itself re-guards it.
/// </summary>
public static class WorkshopReference
{
    // A Steam publishedfileid is a numeric string; bound the length as an untrusted-input guard (matches the client).
    private const int MaxIdLength = 20;

    /// <summary>
    /// Extracts a numeric Workshop id from <paramref name="input"/> — the whole string when it is already a bare
    /// numeric id, otherwise the digits of an <c>id=</c> query parameter in a pasted URL. Returns <c>false</c>
    /// (and an empty id) when no plausible id can be read.
    /// </summary>
    public static bool TryParseId(string? input, out string id)
    {
        id = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string trimmed = input.Trim();
        if (IsNumericId(trimmed))
        {
            id = trimmed;
            return true;
        }

        // Pull the value of an "id=" query parameter without dereferencing the URL. Scan case-insensitively for the
        // first "id=" that starts a parameter (preceded by '?' or '&', or at string start), then read its digits.
        int scan = 0;
        while (scan < trimmed.Length)
        {
            int marker = trimmed.IndexOf("id=", scan, StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
            {
                return false;
            }

            bool startsParameter = marker == 0 || trimmed[marker - 1] is '?' or '&';
            if (startsParameter)
            {
                int start = marker + 3;
                int end = start;
                while (end < trimmed.Length && char.IsAsciiDigit(trimmed[end]) && end - start < MaxIdLength)
                {
                    end++;
                }

                if (end > start)
                {
                    id = trimmed[start..end];
                    return true;
                }
            }

            scan = marker + 3;
        }

        return false;
    }

    private static bool IsNumericId(string value) =>
        value.Length is > 0 and <= MaxIdLength && value.All(char.IsAsciiDigit);
}
