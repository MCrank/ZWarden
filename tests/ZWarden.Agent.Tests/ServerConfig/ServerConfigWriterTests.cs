using System.Text;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ServerConfig;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Revisions;

namespace ZWarden.Agent.Tests.ServerConfig;

/// <summary>
/// F20b PR-3: the Agent-side surgical config write against a real temp directory. The writer re-reads and
/// re-parses the live file, fails the write closed when it has drifted from the recorded baseline (ADR 0011),
/// applies edits byte-preservingly, and writes back BOM-less and atomically — reporting a first-class outcome for
/// every expected condition (absent file, parse failure, an edit that targets no scalar) rather than throwing.
/// </summary>
public class ServerConfigWriterTests
{
    private const string SandboxSrc = """
        SandboxVars = {
            VERSION = 6,
            Zombies = 4, -- population multiplier
            XpMultiplier = 1.0,
            Map = {
                AllowMiniMap = false,
            },
        }
        """;

    private const string IniSrc = "PublicName=My Server\nMaxPlayers=16\n";

    private static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "zwarden-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ServerConfigWriter WriterOver(string root) =>
        new(new PzConfigParser(), Options.Create(new AgentOptions { DataMountRoot = root }));

    private static string Seed(string root, ServerId server, string fileName, string content)
    {
        string path = Path.Combine(root, server.ToString(), "Server", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static string BaselineHash(PzConfigKind kind, string content) =>
        PzValueSnapshot.Of(new PzConfigParser().Open(kind, Encoding.UTF8.GetBytes(content)).Document!).Hash;

    [Test]
    public async Task ApplyAsync_applies_an_edit_and_reports_the_new_snapshot()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            string path = Seed(root, server, "servertest_SandboxVars.lua", SandboxSrc);

            ConfigApplyOutcome outcome = await WriterOver(root).ApplyAsync(
                server, PzConfigFile.SandboxVars, BaselineHash(PzConfigKind.SandboxVars, SandboxSrc),
                [new ConfigValueEdit("Zombies", ConfigValueKind.Number, "1")], CancellationToken.None);

            await Assert.That(outcome.Succeeded).IsTrue();
            await Assert.That(outcome.ChangedCount).IsEqualTo(1);
            string written = await File.ReadAllTextAsync(path);
            await Assert.That(written).Contains("Zombies = 1, -- population multiplier"); // only the value changed
            await Assert.That(written).Contains("VERSION = 6");
            // The reported hash is the new baseline: re-parsing the written file yields the same fingerprint.
            await Assert.That(outcome.SnapshotHash).IsEqualTo(BaselineHash(PzConfigKind.SandboxVars, written));
            await Assert.That(outcome.CanonicalSnapshot).IsNotNull();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task ApplyAsync_fails_closed_when_the_live_file_drifted_from_the_baseline()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            string path = Seed(root, server, "servertest_SandboxVars.lua", SandboxSrc);

            ConfigApplyOutcome outcome = await WriterOver(root).ApplyAsync(
                server, PzConfigFile.SandboxVars, baselineHash: "not-the-current-hash",
                [new ConfigValueEdit("Zombies", ConfigValueKind.Number, "1")], CancellationToken.None);

            await Assert.That(outcome.Succeeded).IsFalse();
            await Assert.That(outcome.Drifted).IsTrue();
            await Assert.That(outcome.FailureReason).IsNotNull();
            // Fail closed: the file on disk is untouched.
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(SandboxSrc);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task ApplyAsync_allows_the_first_write_with_no_baseline()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            Seed(root, server, "servertest_SandboxVars.lua", SandboxSrc);

            ConfigApplyOutcome outcome = await WriterOver(root).ApplyAsync(
                server, PzConfigFile.SandboxVars, baselineHash: null,
                [new ConfigValueEdit("Map.AllowMiniMap", ConfigValueKind.Bool, "true")], CancellationToken.None);

            await Assert.That(outcome.Succeeded).IsTrue();
            await Assert.That(outcome.Drifted).IsFalse();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task ApplyAsync_reports_a_missing_key_as_a_failure_and_leaves_the_file_untouched()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            string path = Seed(root, server, "servertest_SandboxVars.lua", SandboxSrc);

            ConfigApplyOutcome outcome = await WriterOver(root).ApplyAsync(
                server, PzConfigFile.SandboxVars, BaselineHash(PzConfigKind.SandboxVars, SandboxSrc),
                [new ConfigValueEdit("NoSuchKey", ConfigValueKind.Number, "1")], CancellationToken.None);

            await Assert.That(outcome.Succeeded).IsFalse();
            await Assert.That(outcome.Drifted).IsFalse();
            await Assert.That(outcome.FailureReason).Contains("NoSuchKey");
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(SandboxSrc);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task ApplyAsync_fails_when_the_file_is_absent()
    {
        string root = NewRoot();
        try
        {
            ConfigApplyOutcome outcome = await WriterOver(root).ApplyAsync(
                ServerId.New(), PzConfigFile.SandboxVars, baselineHash: null, [], CancellationToken.None);

            await Assert.That(outcome.Succeeded).IsFalse();
            await Assert.That(outcome.FailureReason).Contains("does not exist");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task ApplyAsync_writes_bom_less_bytes()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            // Seed a file that carries a UTF-8 BOM; the write must not restore it (a BOM is fatal to PZ's lexer).
            string path = Path.Combine(root, server.ToString(), "Server", "servertest_SandboxVars.lua");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(SandboxSrc)]);

            ConfigApplyOutcome outcome = await WriterOver(root).ApplyAsync(
                server, PzConfigFile.SandboxVars, baselineHash: null,
                [new ConfigValueEdit("Zombies", ConfigValueKind.Number, "2")], CancellationToken.None);

            await Assert.That(outcome.Succeeded).IsTrue();
            byte[] written = await File.ReadAllBytesAsync(path);
            bool hasBom = written.Length >= 3 && written[0] == 0xEF && written[1] == 0xBB && written[2] == 0xBF;
            await Assert.That(hasBom).IsFalse();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task ApplyAsync_applies_an_ini_edit_on_its_own_line()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            string path = Seed(root, server, "servertest.ini", IniSrc);

            ConfigApplyOutcome outcome = await WriterOver(root).ApplyAsync(
                server, PzConfigFile.Ini, BaselineHash(PzConfigKind.Ini, IniSrc),
                [new ConfigValueEdit("PublicName", ConfigValueKind.Text, "New Name")], CancellationToken.None);

            await Assert.That(outcome.Succeeded).IsTrue();
            string written = await File.ReadAllTextAsync(path);
            await Assert.That(written).Contains("PublicName=New Name");
            await Assert.That(written).Contains("MaxPlayers=16");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task ApplyAsync_rejects_a_malformed_number()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            Seed(root, server, "servertest_SandboxVars.lua", SandboxSrc);

            ConfigApplyOutcome outcome = await WriterOver(root).ApplyAsync(
                server, PzConfigFile.SandboxVars, baselineHash: null,
                [new ConfigValueEdit("Zombies", ConfigValueKind.Number, "not-a-number")], CancellationToken.None);

            await Assert.That(outcome.Succeeded).IsFalse();
            await Assert.That(outcome.FailureReason).Contains("not a valid number");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task ApplyRawAsync_overwrites_the_whole_file_with_valid_text_and_reports_the_snapshot()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            string path = Seed(root, server, "servertest_SandboxVars.lua", SandboxSrc);
            const string edited = "SandboxVars = {\n    VERSION = 6,\n    Zombies = 1,\n    XpMultiplier = 2.0,\n}\n";

            ConfigApplyOutcome outcome = await WriterOver(root).ApplyRawAsync(
                server, PzConfigFile.SandboxVars, BaselineHash(PzConfigKind.SandboxVars, SandboxSrc), edited, CancellationToken.None);

            await Assert.That(outcome.Succeeded).IsTrue();
            // The operator's literal text is written verbatim (raw edit is byte-authoritative, not a re-emit).
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(edited);
            await Assert.That(outcome.SnapshotHash).IsEqualTo(BaselineHash(PzConfigKind.SandboxVars, edited));
            // Zombies 4→1 and XpMultiplier 1.0→2.0 changed; Map was removed — a non-zero value diff.
            await Assert.That(outcome.ChangedCount).IsGreaterThan(0);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task ApplyRawAsync_refuses_text_that_does_not_parse_and_leaves_the_file_untouched()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            string path = Seed(root, server, "servertest_SandboxVars.lua", SandboxSrc);

            // A syntax error (unterminated table) must never be written — it would stop the server on start.
            ConfigApplyOutcome outcome = await WriterOver(root).ApplyRawAsync(
                server, PzConfigFile.SandboxVars, baselineHash: null, "SandboxVars = {\n    Zombies = 1,\n", CancellationToken.None);

            await Assert.That(outcome.Succeeded).IsFalse();
            await Assert.That(outcome.Drifted).IsFalse();
            await Assert.That(outcome.FailureReason).Contains("did not parse");
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(SandboxSrc);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task ApplyRawAsync_fails_closed_when_the_live_file_drifted_from_the_baseline()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            string path = Seed(root, server, "servertest_SandboxVars.lua", SandboxSrc);

            ConfigApplyOutcome outcome = await WriterOver(root).ApplyRawAsync(
                server, PzConfigFile.SandboxVars, baselineHash: "not-the-current-hash",
                "SandboxVars = {\n    Zombies = 1,\n}\n", CancellationToken.None);

            await Assert.That(outcome.Succeeded).IsFalse();
            await Assert.That(outcome.Drifted).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(SandboxSrc);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task ApplyRawAsync_writes_bom_less_even_when_the_edited_text_carries_one()
    {
        string root = NewRoot();
        try
        {
            ServerId server = ServerId.New();
            string path = Seed(root, server, "servertest_SandboxVars.lua", SandboxSrc);

            // The operator's editor prepended a BOM; the write must strip it (a BOM is fatal to PZ's lexer).
            ConfigApplyOutcome outcome = await WriterOver(root).ApplyRawAsync(
                server, PzConfigFile.SandboxVars, baselineHash: null, "﻿SandboxVars = {\n    Zombies = 2,\n}\n", CancellationToken.None);

            await Assert.That(outcome.Succeeded).IsTrue();
            byte[] written = await File.ReadAllBytesAsync(path);
            bool hasBom = written.Length >= 3 && written[0] == 0xEF && written[1] == 0xBB && written[2] == 0xBF;
            await Assert.That(hasBom).IsFalse();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Test]
    public async Task ApplyRawAsync_fails_when_the_file_is_absent()
    {
        string root = NewRoot();
        try
        {
            ConfigApplyOutcome outcome = await WriterOver(root).ApplyRawAsync(
                ServerId.New(), PzConfigFile.SandboxVars, baselineHash: null, "SandboxVars = {}\n", CancellationToken.None);

            await Assert.That(outcome.Succeeded).IsFalse();
            await Assert.That(outcome.FailureReason).Contains("does not exist");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static void TryDelete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
