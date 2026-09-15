namespace ZWarden.Diagnostics.SupportPackage;

/// <summary>
/// The "Validate" stage of the support-package pipeline (F30, PRD 51): a last shape check before packaging. It
/// confirms the package has at least one non-empty document, every entry's checksum lines up with a document, and
/// the secret-scan attestation records a clean run. It is a guard, not the secret gate — the
/// <see cref="SecretScanner"/> is what fails a package on prohibited content; this catches an assembly mistake
/// (an empty or mismatched package) before it is streamed.
/// </summary>
public static class PackageValidator
{
    /// <summary>True when <paramref name="package"/> is well-formed and safe to emit.</summary>
    public static bool IsValid(BuiltSupportPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (package.Documents.Count == 0 || package.Manifest.Entries.Count != package.Documents.Count)
        {
            return false;
        }

        if (!package.Manifest.SecretScan.Ran || !package.Manifest.SecretScan.Clean)
        {
            return false;
        }

        foreach (SupportPackageDocument document in package.Documents)
        {
            if (document.Content.Length == 0)
            {
                return false;
            }

            // Every document must be indexed by a manifest entry whose checksum and size match its bytes.
            SupportPackageManifestEntry? entry = package.Manifest.Entries
                .FirstOrDefault(e => e.Name == document.Name);
            if (entry is null || entry.Sha256 != document.Sha256 || entry.Bytes != document.Content.Length)
            {
                return false;
            }
        }

        return true;
    }
}
