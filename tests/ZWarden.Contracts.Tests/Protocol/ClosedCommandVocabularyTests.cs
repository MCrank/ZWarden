using System.Reflection;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// Turns trust-boundaries.md §9 rule 3 into a red build (F7 test plan 6): <b>no Agent-facing
/// contract carries a free-form command, script or shell string</b>, every message is a sealed leaf
/// of the closed <see cref="AgentCommand"/>/<see cref="AgentEvent"/> vocabulary, and every leaf
/// declares a unique wire discriminator. A reintroduced <c>ExecuteShellCommand(string)</c> — under
/// any name on the denylist — fails this test.
/// </summary>
public class ClosedCommandVocabularyTests
{
    // Property names that would smuggle a free-form executable/script/shell payload across the
    // boundary. A string-typed contract property must never be named any of these (PRD 19). This is
    // a canary: extend it, never weaken it, and only with a recorded reason.
    private static readonly string[] ForbiddenStringMembers =
    [
        "Command", "CommandLine", "Cmd", "Script", "ScriptBody", "Shell",
        "Exec", "Executable", "Arguments", "Args", "Bash", "PowerShell",
    ];

    private static readonly Assembly Contracts = typeof(IProtocolMessage).Assembly;

    private static List<Type> ConcreteMessages() =>
        Contracts.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                && typeof(IProtocolMessage).IsAssignableFrom(t))
            .ToList();

    [Test]
    public async Task No_exported_contract_carries_a_free_form_command_string()
    {
        List<string> violations = [];
        foreach (Type type in Contracts.GetExportedTypes())
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType == typeof(string)
                    && ForbiddenStringMembers.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    violations.Add($"{type.Name}.{property.Name}");
                }
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Every_concrete_message_is_a_sealed_leaf()
    {
        List<string> notSealed = ConcreteMessages().Where(t => !t.IsSealed).Select(t => t.Name).ToList();

        await Assert.That(notSealed).IsEmpty();
    }

    [Test]
    public async Task Every_concrete_message_derives_from_a_closed_root()
    {
        List<string> unrooted = ConcreteMessages()
            .Where(t => !typeof(AgentCommand).IsAssignableFrom(t) && !typeof(AgentEvent).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        await Assert.That(unrooted).IsEmpty();
    }

    [Test]
    public async Task Every_concrete_message_declares_a_wire_discriminator()
    {
        List<string> undeclared = ConcreteMessages()
            .Where(t => t.GetCustomAttribute<ProtocolMessageAttribute>(inherit: false) is null)
            .Select(t => t.Name)
            .ToList();

        await Assert.That(undeclared).IsEmpty();
    }

    [Test]
    public async Task Wire_discriminators_are_unique_and_cover_every_message()
    {
        // ProtocolJson.MessageTypes is built by scanning the assembly; it throws on a duplicate
        // discriminator, so a successful, complete registry is the uniqueness guarantee.
        await Assert.That(ProtocolJson.MessageTypes.Count).IsEqualTo(ConcreteMessages().Count);
    }

    [Test]
    public async Task The_five_lifecycle_messages_are_registered()
    {
        string[] expected =
        [
            "agent.hello", "agent.heartbeat", "agent.state-snapshot",
            "operation.progress", "operation.completed",
        ];

        foreach (string discriminator in expected)
        {
            await Assert.That(ProtocolJson.MessageTypes.ContainsKey(discriminator)).IsTrue();
        }
    }

    [Test]
    public async Task The_diagnostics_ping_is_the_first_command_leaf_and_the_root_stays_closed()
    {
        // F7 shipped the AgentCommand root; F11 adds the first concrete leaf (Diagnostics.Ping). The rest of
        // the mutating vocabulary still lands with the owning features (RestartServer → F15, …).
        List<string> commandLeaves = ConcreteMessages()
            .Where(t => typeof(AgentCommand).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        await Assert.That(typeof(AgentCommand).IsAbstract).IsTrue();
        await Assert.That(commandLeaves).Contains(nameof(PingAgent));
        await Assert.That(ProtocolJson.MessageTypes.ContainsKey("diagnostics.ping")).IsTrue();
    }
}
