using System.Text;
using ZWarden.Agent.Trust;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Agent.Tests;

/// <summary>
/// F9 S8 (PR 2) test plan item 12: the Agent's trust material round-trips through a single local file,
/// written atomically and BOM-less; a malformed file fails typed rather than being silently replaced; and
/// the credential is held as a <see cref="SecretString"/> that never stringifies (ADR 0007).
/// </summary>
public class FileAgentTrustStoreTests
{
    private static AgentTrustMaterial Material(string? label = "host-alpha") =>
        new(AgentId.New(), new SecretString("zwa_test-credential-value"), label);

    [Test]
    public async Task Save_then_load_round_trips()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");
        FileAgentTrustStore store = new(path);
        AgentTrustMaterial saved = Material();

        await store.SaveAsync(saved);
        AgentTrustMaterial? loaded = await store.TryLoadAsync();

        await Assert.That(loaded).IsNotNull();
        await Assert.That(loaded!.AgentId).IsEqualTo(saved.AgentId);
        await Assert.That(loaded.Credential.Reveal()).IsEqualTo(saved.Credential.Reveal());
        await Assert.That(loaded.Label).IsEqualTo("host-alpha");
    }

    [Test]
    public async Task Missing_file_loads_as_null()
    {
        using var temp = new TempDirectory();

        AgentTrustMaterial? loaded = await new FileAgentTrustStore(temp.File("absent.json")).TryLoadAsync();

        await Assert.That(loaded).IsNull();
    }

    [Test]
    public async Task Persisted_file_is_bom_less_and_leaves_no_temp_file()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");

        await new FileAgentTrustStore(path).SaveAsync(Material());

        byte[] bytes = await File.ReadAllBytesAsync(path);
        bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        await Assert.That(hasBom).IsFalse();
        await Assert.That(Directory.GetFiles(temp.Path, "*.tmp")).IsEmpty();
    }

    [Test]
    public async Task Malformed_file_throws_rather_than_being_replaced()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");
        await File.WriteAllTextAsync(path, "{ not valid json");

        await Assert.That(async () => await new FileAgentTrustStore(path).TryLoadAsync())
            .Throws<AgentTrustException>();
    }

    [Test]
    public async Task A_file_missing_the_credential_is_a_typed_failure()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");
        await File.WriteAllTextAsync(path, "{\"AgentId\":\"agt-019c0000000070008000000000000001\",\"Credential\":\"\"}");

        await Assert.That(async () => await new FileAgentTrustStore(path).TryLoadAsync())
            .Throws<AgentTrustException>();
    }

    [Test]
    public async Task The_credential_never_stringifies()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-trust.json");
        FileAgentTrustStore store = new(path);
        await store.SaveAsync(Material());

        AgentTrustMaterial loaded = (await store.TryLoadAsync())!;

        await Assert.That(loaded.Credential.ToString()).IsEqualTo(SecretString.Redacted);
        await Assert.That($"{loaded.Credential}").DoesNotContain("zwa_");
    }
}
