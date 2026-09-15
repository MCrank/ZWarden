using System.Security.Cryptography;

namespace ZWarden.Diagnostics.SupportPackage;

/// <summary>
/// One document destined for a support package (F30): its name, its UTF-8 bytes, and their lowercase-hex SHA-256.
/// The hash is computed over the exact bytes that will be written, so the Web-side ZIP writer can list it in the
/// manifest and a recipient can verify integrity (the F24/ADR 0028 checksum posture). Hashing is pure in-memory
/// computation — the pipeline core performs no I/O.
/// </summary>
/// <param name="Name">The document's name inside the package (e.g. <c>diagnostics.json</c>).</param>
/// <param name="Content">The document's UTF-8 bytes.</param>
/// <param name="Sha256">The lowercase-hex SHA-256 over <see cref="Content"/>.</param>
public sealed record SupportPackageDocument(string Name, byte[] Content, string Sha256)
{
    /// <summary>Creates a document from <paramref name="content"/>, computing its SHA-256.</summary>
    public static SupportPackageDocument Create(string name, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(content);
        return new SupportPackageDocument(name, content, Convert.ToHexStringLower(SHA256.HashData(content)));
    }
}
