namespace ZWarden.Application.Diagnostics;

/// <summary>
/// One diagnostic check's result (F29): a <see cref="DiagnosticStatus"/> for a <see cref="DiagnosticDomain"/>,
/// a short non-secret <see cref="Summary"/>, and an optional <see cref="Detail"/>.
/// <para>
/// <b><see cref="Detail"/> is untrusted</b> (trust-boundaries §8): it may carry text a Server, its mods, its
/// configuration, or its certificate emitted (a mod name, a config value, a certificate subject, a probe error).
/// It is carried verbatim and bounded here, and must be escaped at render (data, never markup). The
/// <see cref="Summary"/> is ZWarden-authored and safe; only <see cref="Detail"/> needs escaping.
/// </para>
/// </summary>
/// <param name="Domain">Which domain this check belongs to.</param>
/// <param name="Status">The verdict.</param>
/// <param name="Summary">A short, ZWarden-authored, non-secret one-line summary.</param>
/// <param name="Detail">Optional, untrusted, bounded detail (an error, a value, a name).</param>
public sealed record DiagnosticCheck(
    DiagnosticDomain Domain,
    DiagnosticStatus Status,
    string Summary,
    string? Detail = null)
{
    /// <summary>The maximum length of <see cref="Detail"/> a check will carry; longer detail is truncated so a
    /// hostile or verbose source cannot bloat a report.</summary>
    public const int MaxDetailLength = 512;

    /// <summary>Creates a check, trimming <paramref name="detail"/> to <see cref="MaxDetailLength"/>. Prefer this
    /// over the constructor whenever the detail originates from an untrusted source.</summary>
    public static DiagnosticCheck Create(DiagnosticDomain domain, DiagnosticStatus status, string summary, string? detail = null)
    {
        string? bounded = detail is { Length: > MaxDetailLength } ? detail[..MaxDetailLength] : detail;
        return new DiagnosticCheck(domain, status, summary, bounded);
    }
}
