namespace ZWarden.ArchitectureTests;

/// <summary>
/// F3 (ADR 0015) architecture guards, turned into a red build. Two things must stay true no matter
/// what later features add: the obsolete-crypto compiler diagnostics are never silenced, and the raw
/// AEAD primitives stay confined to the secret-protection code rather than spreading through the
/// codebase. Source-text scans, in the style of <see cref="DeferredTransactionGuardTests"/>.
/// </summary>
public class SecretHandlingGuardTests
{
    // The .NET 10 obsoletions that keep secret crypto honest: SYSLIB0053 (tag-size-less AesGcm ctor)
    // and SYSLIB0060 (Rfc2898DeriveBytes ctors). Warnings are errors (ADR 0013), so using them fails
    // the build; the only way past is to silence them, which this forbids.
    private static readonly string[] ForbiddenCryptoSuppressions =
    [
        "SYSLIB0053",
        "SYSLIB0060",
    ];

    // Raw AEAD / KDF primitives. They belong only to the secret protector, never scattered around.
    private static readonly string[] RawCryptoPrimitives =
    [
        "AesGcm",
        "ChaCha20Poly1305",
        "HKDF",
    ];

    // Ways a diagnostic gets silenced. A plain comment naming the id (to document why we avoid the
    // obsolete API) is allowed; actually turning the warning off is not.
    private static readonly string[] SuppressionKeywords =
    [
        "disable", "NoWarn", "SuppressMessage", "WarningsNotAsErrors", "WarningsAsErrors",
    ];

    [Test]
    public async Task The_obsolete_crypto_diagnostics_are_never_suppressed()
    {
        List<string> violations = [];
        foreach (string file in SourceFiles("*.cs").Concat(SourceFiles("*.csproj")))
        {
            foreach (string line in await File.ReadAllLinesAsync(file))
            {
                bool namesDiagnostic = ForbiddenCryptoSuppressions.Any(id => line.Contains(id, StringComparison.Ordinal));
                bool silences = SuppressionKeywords.Any(k => line.Contains(k, StringComparison.Ordinal));
                if (namesDiagnostic && silences)
                {
                    violations.Add($"obsolete crypto diagnostic silenced in {Path.GetFileName(file)}: {line.Trim()}");
                }
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task No_obsolete_pbkdf2_constructor_is_used()
    {
        List<string> violations = [];
        foreach (string file in SourceFiles("*.cs"))
        {
            string text = await File.ReadAllTextAsync(file);
            if (text.Contains("new Rfc2898DeriveBytes(", StringComparison.Ordinal))
            {
                violations.Add($"'new Rfc2898DeriveBytes(' in {Path.GetFileName(file)} - use the static Rfc2898DeriveBytes.Pbkdf2(...).");
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Raw_aead_primitives_are_confined_to_the_secret_protection_code()
    {
        string allowedPrefix = Path.Combine("ZWarden.Infrastructure", "Security") + Path.DirectorySeparatorChar;
        List<string> violations = [];

        foreach (string file in SourceFiles("*.cs"))
        {
            if (file.Contains(allowedPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string text = await File.ReadAllTextAsync(file);
            foreach (string primitive in RawCryptoPrimitives)
            {
                if (text.Contains(primitive, StringComparison.Ordinal))
                {
                    violations.Add($"'{primitive}' in {RelativeToSrc(file)} - AEAD primitives belong only under ZWarden.Infrastructure/Security.");
                }
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    private static IEnumerable<string> SourceFiles(string pattern)
    {
        string src = Path.Combine(RepoRoot(), "src");
        foreach (string file in Directory.EnumerateFiles(src, pattern, SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return file;
        }
    }

    private static string RelativeToSrc(string file) =>
        Path.GetRelativePath(Path.Combine(RepoRoot(), "src"), file);

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ZWarden.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (ZWarden.slnx).");
    }
}
