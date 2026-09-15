using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using ZWarden.Diagnostics.SupportPackage;

namespace ZWarden.Web.Diagnostics;

/// <summary>
/// Writes a built support package (F30) into a ZIP: a <c>manifest.json</c> index followed by every document, in a
/// stable order. This is the <b>only</b> I/O in the F30 path — the sanitize/redact/scan pipeline is pure and lives
/// in <c>ZWarden.Diagnostics</c> (F30 D-1), so <see cref="System.IO.Compression"/> stays here, Web-side. The bytes
/// each document carries are already the exact, checksummed bytes the manifest lists, so a recipient can verify
/// integrity against <see cref="SupportPackageManifestEntry.Sha256"/>.
/// </summary>
public static class ZipSupportPackageWriter
{
    /// <summary>The manifest's name inside the ZIP.</summary>
    public const string ManifestName = "manifest.json";

    // Relaxed escaping keeps the manifest readable (matches the pipeline's diagnostics.json). The manifest is
    // ZWarden-authored (ids, hashes, sanitized env facts) — never rendered as HTML.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Serializes <paramref name="package"/> to ZIP bytes: the manifest first, then each document under its
    /// name. Deterministic — the entry order and content are a pure function of the package.</summary>
    public static byte[] Write(BuiltSupportPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        using MemoryStream buffer = new();
        // leaveOpen so the archive's Dispose flushes the central directory before we read the buffer.
        using (ZipArchive archive = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(package.Manifest, Json);
            WriteEntry(archive, ManifestName, manifestBytes);

            foreach (SupportPackageDocument document in package.Documents)
            {
                WriteEntry(archive, document.Name, document.Content);
            }
        }

        return buffer.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        stream.Write(content);
    }
}
