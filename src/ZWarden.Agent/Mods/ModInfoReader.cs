using System.Text;

namespace ZWarden.Agent.Mods;

/// <summary>A mod declared by a <c>mod.info</c> (F21): its Mod id (the <c>id=</c> value — the token the config's
/// <c>Mods=</c> line enables) and the display name (<c>name=</c>) when present. Both are untrusted, bounded, and
/// carried verbatim.</summary>
/// <param name="Id">The PZ Mod id.</param>
/// <param name="Name">The declared display name, or <c>null</c>.</param>
public sealed record ModInfo(string Id, string? Name);

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

    /// <summary>The maximum stored length of a single field (id or name); longer values are truncated.</summary>
    public const int MaxFieldLength = 256;

    /// <summary>Parses <paramref name="bytes"/> into a <see cref="ModInfo"/>, or <c>null</c> when the content is
    /// oversized or declares no usable <c>id</c>. The first <c>id</c>/<c>name</c> encountered wins.</summary>
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

            if (id is null && key.Equals("id", StringComparison.OrdinalIgnoreCase) && !value.IsEmpty)
            {
                id = Bound(value);
            }
            else if (name is null && key.Equals("name", StringComparison.OrdinalIgnoreCase) && !value.IsEmpty)
            {
                name = Bound(value);
            }
        }

        return id is null ? null : new ModInfo(id, name);
    }

    private static string Bound(ReadOnlySpan<char> value) =>
        value.Length <= MaxFieldLength ? value.ToString() : value[..MaxFieldLength].ToString();
}
