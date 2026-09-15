using System.Text;
using ZWarden.Diagnostics.SupportPackage;

namespace ZWarden.Diagnostics.Tests.SupportPackage;

/// <summary>F30 PR-A: the "Validate" stage rejects an empty, mismatched, or un-scanned assembly before it is
/// streamed. It is a shape guard, not the secret gate (that is <see cref="SecretScanner"/>).</summary>
public class PackageValidatorTests
{
    private static readonly EnvironmentFacts Env = new("1.0.0", "Sqlite", "Linux", ".NET 10.0");
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static SupportPackageDocument Doc() =>
        SupportPackageDocument.Create("diagnostics.json", Encoding.UTF8.GetBytes("{\"ok\":true}"));

    private static SupportPackageManifest Manifest(SupportPackageDocument doc, bool clean = true) => new(
        DiagnosticId: "diag-0193f0a1-2b3c-4d5e-8f90-1a2b3c4d5e6f",
        SchemaVersion: 1,
        GeneratedAtUtc: Now,
        Environment: Env,
        Scope: "tenant",
        WorstStatus: "Pass",
        Entries: [new SupportPackageManifestEntry(doc.Name, doc.Sha256, doc.Content.Length)],
        SecretScan: new SecretScanAttestation(Ran: true, Clean: clean));

    [Test]
    public async Task A_well_formed_package_is_valid()
    {
        SupportPackageDocument doc = Doc();

        await Assert.That(PackageValidator.IsValid(new BuiltSupportPackage(Manifest(doc), [doc]))).IsTrue();
    }

    [Test]
    public async Task A_package_with_no_documents_is_invalid()
    {
        SupportPackageManifest manifest = Manifest(Doc()) with { Entries = [] };

        await Assert.That(PackageValidator.IsValid(new BuiltSupportPackage(manifest, []))).IsFalse();
    }

    [Test]
    public async Task A_checksum_mismatch_is_invalid()
    {
        SupportPackageDocument doc = Doc();
        SupportPackageManifest manifest = Manifest(doc) with
        {
            Entries = [new SupportPackageManifestEntry(doc.Name, "deadbeef", doc.Content.Length)],
        };

        await Assert.That(PackageValidator.IsValid(new BuiltSupportPackage(manifest, [doc]))).IsFalse();
    }

    [Test]
    public async Task An_unclean_scan_attestation_is_invalid()
    {
        SupportPackageDocument doc = Doc();

        await Assert.That(PackageValidator.IsValid(new BuiltSupportPackage(Manifest(doc, clean: false), [doc]))).IsFalse();
    }
}
