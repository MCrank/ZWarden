using ZWarden.Diagnostics.SupportPackage;

namespace ZWarden.Diagnostics.Tests.SupportPackage;

/// <summary>
/// F30 PR-A: the fail-closed secret gate (D-3, PRD 51). Each detector fires on its shape and, crucially, benign
/// diagnostic text does <b>not</b> fire — a scanner that flagged everything would fail every package. A detection
/// reports only the detector name and offset, never the matched value.
/// </summary>
public class SecretScannerTests
{
    [Test]
    public async Task Clean_text_is_null()
    {
        SecretDetection? detection = SecretScanner.Scan(
            "Mod 'Brita's Weapons' failed to load: missing dependency 'Base.Vanilla'. Server at world spawn.");

        await Assert.That(detection).IsNull();
    }

    [Test]
    public async Task Empty_text_is_null()
    {
        await Assert.That(SecretScanner.Scan("")).IsNull();
        await Assert.That(SecretScanner.Scan(null)).IsNull();
    }

    [Test]
    public async Task A_pem_private_key_is_detected()
    {
        SecretDetection? detection = SecretScanner.Scan(
            "config error near -----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA...");

        await Assert.That(detection).IsNotNull();
        await Assert.That(detection!.DetectorName).IsEqualTo("pem-private-key");
    }

    [Test]
    public async Task An_openssh_private_key_is_detected()
    {
        SecretDetection? detection = SecretScanner.Scan("-----BEGIN OPENSSH PRIVATE KEY-----");

        await Assert.That(detection!.DetectorName).IsEqualTo("pem-private-key");
    }

    [Test]
    public async Task A_jwt_is_detected()
    {
        SecretDetection? detection = SecretScanner.Scan(
            "token=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U");

        await Assert.That(detection!.DetectorName).IsEqualTo("jwt");
    }

    [Test]
    public async Task An_aws_access_key_is_detected()
    {
        SecretDetection? detection = SecretScanner.Scan("key AKIAIOSFODNN7EXAMPLE in the log");

        await Assert.That(detection!.DetectorName).IsEqualTo("aws-access-key");
    }

    [Test]
    public async Task A_github_token_is_detected()
    {
        SecretDetection? detection = SecretScanner.Scan("ghp_EXAMPLEEXAMPLEEXAMPLEEXAMPLE000000");

        await Assert.That(detection!.DetectorName).IsEqualTo("github-token");
    }

    [Test]
    public async Task A_slack_token_is_detected()
    {
        // A deliberately fake token: matches the shape the scanner catches, but not a real Slack credential.
        SecretDetection? detection = SecretScanner.Scan("xoxb-EXAMPLE-NOT-A-REAL-SLACK-TOKEN-000000");

        await Assert.That(detection!.DetectorName).IsEqualTo("slack-token");
    }

    [Test]
    public async Task A_bearer_token_is_detected()
    {
        SecretDetection? detection = SecretScanner.Scan("Authorization: Bearer abcdef0123456789ABCDEF");

        await Assert.That(detection!.DetectorName).IsEqualTo("bearer-token");
    }

    [Test]
    public async Task Url_embedded_credentials_are_detected()
    {
        SecretDetection? detection = SecretScanner.Scan("postgres://zwarden:s3cr3tP4ss@db.internal:5432/zwarden");

        await Assert.That(detection!.DetectorName).IsEqualTo("url-credentials");
    }

    [Test]
    public async Task An_inline_password_is_detected()
    {
        SecretDetection? detection = SecretScanner.Scan("connect with password=hunter2secret and retry");

        await Assert.That(detection!.DetectorName).IsEqualTo("inline-password");
    }

    [Test]
    public async Task A_masked_inline_password_does_not_fire()
    {
        // After redaction the value is the mask; the scanner must not re-flag it.
        await Assert.That(SecretScanner.Scan("password=***")).IsNull();
    }

    [Test]
    public async Task A_high_entropy_token_is_detected()
    {
        SecretDetection? detection = SecretScanner.Scan(
            "leftover value bG9uZ1JhbmQ3a2V5VmFsdWVXaXRoSGlnaEVudHJvcHlYWVo5OA in detail");

        await Assert.That(detection!.DetectorName).IsEqualTo("high-entropy");
    }

    [Test]
    public async Task A_sha256_hex_digest_does_not_fire_as_high_entropy()
    {
        // We legitimately emit SHA-256 digests (manifest checksums); a 64-hex run must not be treated as a secret.
        await Assert.That(SecretScanner.Scan("sha256=" + new string('a', 32) + "b0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5")).IsNull();
    }

    [Test]
    public async Task A_guid_does_not_fire_as_high_entropy()
    {
        await Assert.That(SecretScanner.Scan("id diag-0193f0a1-2b3c-4d5e-8f90-1a2b3c4d5e6f done")).IsNull();
    }

    [Test]
    public async Task A_long_ordinary_sentence_does_not_fire()
    {
        await Assert.That(SecretScanner.Scan(
            "The server configuration file parsed successfully and every enabled mod resolved against the installed build."))
            .IsNull();
    }
}
