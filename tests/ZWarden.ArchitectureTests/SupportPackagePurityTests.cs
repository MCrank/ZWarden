using System.Reflection;
using ZWarden.Diagnostics.SupportPackage;

namespace ZWarden.ArchitectureTests;

/// <summary>
/// F30 D-1: the support-package pipeline is a <b>pure</b>, I/O-free core so the security-critical
/// transform-and-gate logic (sanitize/redact/pseudonymize/secret-scan) can be exhaustively unit-tested and cannot
/// regress behind I/O. The ZIP and the HTTP download are the Web-side writer's job, in a different assembly. This
/// proves the <c>ZWarden.Diagnostics</c> assembly references neither the ZIP nor the HTTP BCL assembly — a future
/// <c>ZipArchive</c> or <c>HttpClient</c> in the core would pull one in and turn this red.
/// </summary>
public class SupportPackagePurityTests
{
    private static readonly Assembly DiagnosticsAssembly = typeof(ISupportPackageBuilder).Assembly;

    private static readonly string[] ForbiddenAssemblies =
    [
        "System.IO.Compression",
        "System.Net.Http",
    ];

    [Test]
    public async Task The_pipeline_assembly_references_no_zip_or_http_assembly()
    {
        List<string> leaks = DiagnosticsAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null && ForbiddenAssemblies.Contains(name, StringComparer.Ordinal))
            .Select(name => name!)
            .ToList();

        await Assert.That(leaks).IsEmpty();
    }
}
