using System.Text;
using ZWarden.Agent.Identity;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests;

/// <summary>
/// F8 test plan items 1-3: file-based self-identity is generated once and persisted, reloaded
/// verbatim, atomically and BOM-lessly written, and never silently replaced when malformed.
/// </summary>
public class FileAgentIdentityStoreTests
{
    private static FileAgentIdentityStore Store(string path) =>
        new(path, new RecordingLogger<FileAgentIdentityStore>());

    [Test]
    public async Task First_run_generates_persists_and_is_stable_across_restart()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-id.txt");

        AgentId created = await Store(path).LoadOrCreateAsync();

        await Assert.That(File.Exists(path)).IsTrue();

        // A fresh store over the same file reloads the same id rather than minting a new one.
        AgentId reloaded = await Store(path).LoadOrCreateAsync();
        await Assert.That(reloaded).IsEqualTo(created);
    }

    [Test]
    public async Task Existing_file_is_loaded_verbatim()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-id.txt");
        AgentId seeded = AgentId.New();
        await File.WriteAllTextAsync(path, seeded.ToString());

        AgentId? loaded = await Store(path).TryLoadAsync();

        await Assert.That(loaded).IsEqualTo(seeded);
    }

    [Test]
    public async Task Missing_file_loads_as_null()
    {
        using var temp = new TempDirectory();

        AgentId? loaded = await Store(temp.File("absent.txt")).TryLoadAsync();

        await Assert.That(loaded).IsNull();
    }

    [Test]
    public async Task Malformed_file_throws_rather_than_minting_a_new_identity()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-id.txt");
        await File.WriteAllTextAsync(path, "this-is-not-an-agent-id");

        await Assert.That(async () => await Store(path).TryLoadAsync()).Throws<AgentIdentityException>();
    }

    [Test]
    public async Task Persisted_file_is_bom_less_canonical_and_leaves_no_temp_file()
    {
        using var temp = new TempDirectory();
        string path = temp.File("agent-id.txt");

        AgentId created = await Store(path).LoadOrCreateAsync();

        byte[] bytes = await File.ReadAllBytesAsync(path);
        bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        await Assert.That(hasBom).IsFalse();

        string text = Encoding.UTF8.GetString(bytes);
        await Assert.That(text).IsEqualTo(created.ToString());

        string[] strays = Directory.GetFiles(temp.Path, "*.tmp");
        await Assert.That(strays).IsEmpty();
    }
}
