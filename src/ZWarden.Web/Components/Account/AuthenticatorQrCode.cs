using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using QRCoder;

namespace ZWarden.Web.Components.Account;

/// <summary>
/// Builds the authenticator enrolment payload for the static-rendered MFA page (F4 UI, issue #63):
/// the <c>otpauth://</c> URI an authenticator app is seeded with, and an inline SVG QR of it rendered
/// <b>server-side</b> (QRCoder's managed <see cref="SvgQRCode"/>). Server-side rendering is what lets
/// the enrolment page stay static — no JS interop, no client-side QR library, no outbound internet.
/// </summary>
/// <remarks>
/// The shared key is a secret: it appears here only to seed the user's own authenticator, is never
/// logged, and lives only in the one response that shows it. Callers pass the unformatted base32 key
/// from <c>MfaService.GetOrCreateAuthenticatorKeyAsync</c>.
/// </remarks>
internal static class AuthenticatorQrCode
{
    /// <summary>Builds the <c>otpauth://totp/…</c> URI (issuer + account, with the shared secret).</summary>
    public static string BuildOtpauthUri(string issuer, string email, string unformattedKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(unformattedKey);

        string encodedIssuer = Uri.EscapeDataString(issuer);
        string encodedEmail = Uri.EscapeDataString(email);
        return string.Format(
            CultureInfo.InvariantCulture,
            "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
            encodedIssuer,
            encodedEmail,
            unformattedKey);
    }

    /// <summary>Renders <paramref name="otpauthUri"/> as an inline SVG QR (a <see cref="MarkupString"/>
    /// ready to drop into markup).</summary>
    public static MarkupString RenderSvg(string otpauthUri, int pixelsPerModule = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(otpauthUri);

        using QRCodeGenerator generator = new();
        using QRCodeData data = generator.CreateQrCode(otpauthUri, QRCodeGenerator.ECCLevel.Q);
        using SvgQRCode qr = new(data);
        return new MarkupString(qr.GetGraphic(pixelsPerModule));
    }

    /// <summary>Groups the unformatted key into space-separated blocks of four for manual entry
    /// (e.g. <c>ABCD EFGH IJKL</c>).</summary>
    public static string FormatKey(string unformattedKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unformattedKey);

        StringBuilder result = new();
        for (int i = 0; i < unformattedKey.Length; i += 4)
        {
            if (i > 0)
            {
                result.Append(' ');
            }

            int length = Math.Min(4, unformattedKey.Length - i);
            result.Append(unformattedKey.AsSpan(i, length));
        }

        return result.ToString().ToLowerInvariant();
    }
}
