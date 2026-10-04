using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Domain.Security;

namespace ZWarden.Web.Hosting;

/// <summary>
/// #186 / ADR 0015: encrypts the ASP.NET Core Data Protection key ring at rest with ZWarden's own
/// AES-256-GCM key ring (<see cref="ISecretProtector"/>), instead of leaving the persisted keys in
/// plaintext (the "No XML encryptor configured" startup warning). Each Data Protection key XML element
/// is wrapped in a versioned envelope by the app's key ring; the paired <see cref="SecretProtectorXmlDecryptor"/>
/// (named in the emitted element) unwraps it when the ring is loaded.
/// </summary>
public sealed class SecretProtectorXmlEncryptor : IXmlEncryptor
{
    /// <summary>The XML element name the encrypted envelope is stored under (read back by the decryptor).</summary>
    internal const string ValueElementName = "value";

    private readonly ISecretProtector _protector;

    /// <summary>Creates the encryptor over the app's secret protector (the AES key ring, ADR 0015).</summary>
    public SecretProtectorXmlEncryptor(ISecretProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        _protector = protector;
    }

    /// <inheritdoc />
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        string envelope = _protector.ProtectString(plaintextElement.ToString(SaveOptions.DisableFormatting));
        XElement encryptedElement = new(
            "encryptedKey",
            new XComment(" Encrypted with ZWarden's AES-256-GCM key ring (ADR 0015). "),
            new XElement(ValueElementName, envelope));

        return new EncryptedXmlInfo(encryptedElement, typeof(SecretProtectorXmlDecryptor));
    }
}

/// <summary>
/// #186 / ADR 0015: unwraps a Data Protection key element that <see cref="SecretProtectorXmlEncryptor"/>
/// encrypted with the app's AES key ring. Data Protection builds it by type name (the name the encryptor
/// writes into each key element) through its <c>IActivator</c>, which only passes an
/// <see cref="IServiceProvider"/>. So the activation constructor takes the container and resolves the
/// same <see cref="ISecretProtector"/> singleton the encryptor used (#306).
/// </summary>
public sealed class SecretProtectorXmlDecryptor : IXmlDecryptor
{
    private readonly ISecretProtector _protector;

    /// <summary>
    /// The constructor Data Protection's activator calls when it reads a persisted key (#306). Without it,
    /// every restart failed to decrypt the key ring and minted a new key, which signed everyone out.
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public SecretProtectorXmlDecryptor(IServiceProvider services)
        : this(ResolveProtector(services))
    {
    }

    /// <summary>Creates the decryptor over the app's secret protector (the AES key ring, ADR 0015).</summary>
    public SecretProtectorXmlDecryptor(ISecretProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        _protector = protector;
    }

    /// <inheritdoc />
    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        XElement value = encryptedElement.Element(SecretProtectorXmlEncryptor.ValueElementName)
            ?? throw new InvalidOperationException(
                "The encrypted Data Protection key element is missing its envelope value.");

        string xml = _protector.UnprotectString(value.Value);
        return XElement.Parse(xml, LoadOptions.PreserveWhitespace);
    }

    private static ISecretProtector ResolveProtector(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<ISecretProtector>();
    }
}
