using System.Text;

namespace ZWarden.PzConfig.Internal;

/// <summary>
/// Byte→text helpers shared by the readers. Project Zomboid's config files are UTF-8; a leading
/// UTF-8 BOM is a sharp trap — it is <em>fatal</em> to the game's own lexer (research §3, ADR 0010) —
/// so ZWarden strips it on read to still model the file, and (in F20b) must never write one back.
/// </summary>
internal static class PzText
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary><see langword="true"/> if the bytes begin with a UTF-8 BOM.</summary>
    public static bool HasUtf8Bom(ReadOnlySpan<byte> bytes) => bytes.StartsWith(Utf8Bom);

    /// <summary>Decodes UTF-8, stripping a single leading BOM if present.</summary>
    public static string DecodeUtf8(ReadOnlySpan<byte> bytes)
    {
        if (HasUtf8Bom(bytes))
        {
            bytes = bytes[Utf8Bom.Length..];
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
