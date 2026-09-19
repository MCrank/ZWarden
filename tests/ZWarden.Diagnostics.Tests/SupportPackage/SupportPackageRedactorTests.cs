using ZWarden.Diagnostics.SupportPackage;
using ZWarden.Domain.Security;

namespace ZWarden.Diagnostics.Tests.SupportPackage;

/// <summary>F30 PR-A: the "Redact" stage masks values behind a sensitive key, reusing the F3 <see cref="Redaction"/>
/// vocabulary, and leaves benign pairs and free text alone.</summary>
public class SupportPackageRedactorTests
{
    [Test]
    public async Task An_rcon_password_assignment_is_masked()
    {
        await Assert.That(SupportPackageRedactor.RedactText("RconPassword=hunter2"))
            .IsEqualTo("RconPassword=" + Redaction.Mask);
    }

    [Test]
    public async Task A_token_with_colon_and_spaces_is_masked()
    {
        await Assert.That(SupportPackageRedactor.RedactText("token: abc123def"))
            .IsEqualTo("token: " + Redaction.Mask);
    }

    [Test]
    public async Task A_connection_string_key_is_masked()
    {
        await Assert.That(SupportPackageRedactor.RedactText("ConnectionString=secretdsn"))
            .IsEqualTo("ConnectionString=" + Redaction.Mask);
    }

    [Test]
    public async Task Masking_stops_at_a_semicolon_delimiter()
    {
        // A sensitive key's value is masked up to the next delimiter; a following benign pair is untouched
        // (and any sensitive pair after it is caught by its own key).
        await Assert.That(SupportPackageRedactor.RedactText("password=abc123;Port=5432"))
            .IsEqualTo("password=" + Redaction.Mask + ";Port=5432");
    }

    [Test]
    public async Task A_steam_workshop_api_key_is_masked()
    {
        // F110/ADR 0044: the tenant's Steam Web API search key must never survive into a support package.
        await Assert.That(SupportPackageRedactor.RedactText("SteamWebApiKey=ABCDEF0123456789ABCDEF0123456789"))
            .IsEqualTo("SteamWebApiKey=" + Redaction.Mask);
    }

    [Test]
    public async Task A_benign_key_passes_through()
    {
        await Assert.That(SupportPackageRedactor.RedactText("MaxPlayers=32")).IsEqualTo("MaxPlayers=32");
    }

    [Test]
    public async Task Free_text_with_no_pair_passes_through()
    {
        const string text = "The mod failed to resolve its dependency.";

        await Assert.That(SupportPackageRedactor.RedactText(text)).IsEqualTo(text);
    }

    [Test]
    public async Task An_already_masked_value_is_left_unchanged()
    {
        await Assert.That(SupportPackageRedactor.RedactText("password=" + Redaction.Mask))
            .IsEqualTo("password=" + Redaction.Mask);
    }

    [Test]
    public async Task Null_becomes_empty()
    {
        await Assert.That(SupportPackageRedactor.RedactText(null)).IsEqualTo("");
    }
}
