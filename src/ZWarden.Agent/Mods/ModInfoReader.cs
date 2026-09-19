using System.Text;

namespace ZWarden.Agent.Mods;

/// <summary>A mod declared by a <c>mod.info</c> (F21): its Mod id (the <c>id=</c> value — the token the config's
/// <c>Mods=</c> line enables), the display name (<c>name=</c>), and (#110) the optional version, dependency, and
/// compatibility metadata the file declares (research §6). Every field is untrusted, bounded, and carried verbatim;
/// the list fields default to empty, never <c>null</c>.</summary>
/// <param name="Id">The PZ Mod id.</param>
/// <param name="Name">The declared display name, or <c>null</c>.</param>
/// <param name="Version">The mod's own version (<c>version=</c>), or <c>null</c>.</param>
/// <param name="PzVersion">The declared Project Zomboid version (<c>pzversion=</c>), or <c>null</c>.</param>
/// <param name="VersionMin">The declared minimum PZ version (<c>versionMin=</c>), or <c>null</c>.</param>
/// <param name="Requires">The Mod ids this mod depends on (<c>require=</c>); empty when none.</param>
/// <param name="Incompatible">The Mod ids this mod declares incompatible (<c>incompatible=</c>); empty when none.</param>
/// <param name="Tags">The mod's declared tags (<c>tags=</c>); empty when none.</param>
public sealed record ModInfo(
    string Id,
    string? Name,
    string? Version = null,
    string? PzVersion = null,
    string? VersionMin = null,
    IReadOnlyList<string>? Requires = null,
    IReadOnlyList<string>? Incompatible = null,
    IReadOnlyList<string>? Tags = null)
{
    /// <summary>The Mod ids this mod depends on (<c>require=</c>); empty when none.</summary>
    public IReadOnlyList<string> Requires { get; init; } = Requires ?? [];

    /// <summary>The Mod ids this mod declares incompatible (<c>incompatible=</c>); empty when none.</summary>
    public IReadOnlyList<string> Incompatible { get; init; } = Incompatible ?? [];

    /// <summary>The mod's declared tags (<c>tags=</c>); empty when none.</summary>
    public IReadOnlyList<string> Tags { get; init; } = Tags ?? [];
}

/// <summary>
/// Reads a Project Zomboid <c>mod.info</c> — a flat UTF-8 <c>key=value</c> text file, never Lua and not one of PZ's
/// four config files (F21). Only the <c>id=</c> and <c>name=</c> keys are needed for discovery and mapping. The
/// content is <b>untrusted</b> (trust-boundaries.md §8), so the reader is defensive: it caps the byte size
/// <b>before</b> parsing, tolerates a leading UTF-8 BOM, bounds every field length, skips malformed lines instead of
/// throwing, and returns <c>null</c> when there is no usable <c>id</c> (nothing to map).
/// </summary>
public static class ModInfoReader
{
    /// <summary>The largest <c>mod.info</c> the reader will parse; anything larger is rejected unread. Real files are
    /// a few hundred bytes — this is a generous untrusted-input guard, not a tuning knob.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>The maximum stored length of a single field (id, name, or one list element); longer values are
    /// truncated.</summary>
    public const int MaxFieldLength = 256;

    /// <summary>The maximum number of elements kept in a single list field (<c>require</c>/<c>incompatible</c>/
    /// <c>tags</c>); extra elements are dropped. An untrusted-input guard, not a tuning knob — real files carry a
    /// handful.</summary>
    public const int MaxListItems = 128;

    /// <summary>Parses <paramref name="bytes"/> into a <see cref="ModInfo"/>, or <c>null</c> when the content is
    /// oversized or declares no usable <c>id</c>. Scalar fields (<c>id</c>/<c>name</c>/<c>version</c>/
    /// <c>pzversion</c>/<c>versionMin</c>) take the first value seen; list fields (<c>require</c>/<c>incompatible</c>/
    /// <c>tags</c>) accumulate across repeated and comma-separated values, in order.</summary>
    public static ModInfo? Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > MaxBytes)
        {
            return null;
        }

        // Decode as UTF-8 (Encoding.UTF8 replaces invalid sequences rather than throwing), dropping a leading BOM.
        string text = Encoding.UTF8.GetString(bytes);
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        string? id = null;
        string? name = null;
        string? version = null;
        string? pzVersion = null;
        string? versionMin = null;
        List<string> requires = [];
        List<string> incompatible = [];
        List<string> tags = [];

        foreach (ReadOnlySpan<char> rawLine in text.AsSpan().EnumerateLines())
        {
            int equals = rawLine.IndexOf('=');
            if (equals <= 0)
            {
                // No '=', or an empty key ("=value"): a malformed line — skip it.
                continue;
            }

            ReadOnlySpan<char> key = rawLine[..equals].Trim();
            ReadOnlySpan<char> value = rawLine[(equals + 1)..].Trim();

            if (key.Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                if (id is null && !value.IsEmpty)
                {
                    id = Bound(value);
                }
            }
            else if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                if (name is null && !value.IsEmpty)
                {
                    name = Bound(value);
                }
            }
            else if (key.Equals("version", StringComparison.OrdinalIgnoreCase))
            {
                version ??= value.IsEmpty ? null : Bound(value);
            }
            else if (key.Equals("pzversion", StringComparison.OrdinalIgnoreCase))
            {
                pzVersion ??= value.IsEmpty ? null : Bound(value);
            }
            else if (key.Equals("versionMin", StringComparison.OrdinalIgnoreCase))
            {
                versionMin ??= value.IsEmpty ? null : Bound(value);
            }
            else if (key.Equals("require", StringComparison.OrdinalIgnoreCase))
            {
                AppendList(requires, value);
            }
            else if (key.Equals("incompatible", StringComparison.OrdinalIgnoreCase))
            {
                AppendList(incompatible, value);
            }
            else if (key.Equals("tags", StringComparison.OrdinalIgnoreCase))
            {
                AppendList(tags, value);
            }
        }

        return id is null
            ? null
            : new ModInfo(id, name, version, pzVersion, versionMin, requires, incompatible, tags);
    }

    private static string Bound(ReadOnlySpan<char> value) =>
        value.Length <= MaxFieldLength ? value.ToString() : value[..MaxFieldLength].ToString();

    // Splits a value on ',' and appends each non-empty, bounded element, stopping at the per-field element cap.
    private static void AppendList(List<string> into, ReadOnlySpan<char> value)
    {
        foreach (Range segment in value.Split(','))
        {
            if (into.Count >= MaxListItems)
            {
                return;
            }

            ReadOnlySpan<char> element = value[segment].Trim();
            if (!element.IsEmpty)
            {
                into.Add(Bound(element));
            }
        }
    }
}
