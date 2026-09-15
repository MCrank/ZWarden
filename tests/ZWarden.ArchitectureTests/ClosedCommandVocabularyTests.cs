using System.Collections;
using System.Reflection;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.ArchitectureTests;

/// <summary>
/// trust-boundaries.md §9 rule 3, turned into a red build: <b>no Agent-facing command carries a free-form
/// command, script or shell string.</b> The Web → Agent vocabulary is closed (PRD 19, ADR 0020): every
/// command is a <see cref="AgentCommand"/> from a finite set, and <c>ExecuteShellCommand(string)</c> "cannot
/// exist here under any name" (trust-boundaries.md §3). This is the type-level rule that F7 promised and F40
/// finally owns, now that every command in the vocabulary has shipped.
///
/// The guard reflects over every concrete <see cref="AgentCommand"/> and every payload record reachable from
/// one, and fails if a text-bearing member is <i>named</i> like shell/script execution. The one command that
/// legitimately carries an operator-authored line — <see cref="ExecuteConsoleCommand"/> (F28, policy-gated
/// RCON, not a shell — ADR 0032) — names its field <c>Input</c> precisely so it is not mistakable for a
/// free-form command; that naming is itself asserted below as the canary.
/// </summary>
public class ClosedCommandVocabularyTests
{
    private static readonly Assembly ContractsAssembly = typeof(AgentCommand).Assembly;

    // Camel-split words that signal OS/shell/script execution. A text member of a command named with any of
    // these is a free-form-command smell (trust-boundaries.md §3). The list mirrors the one the
    // ExecuteConsoleCommand doc-comment names when it explains why its field is "Input", not "Command"/"Cmd".
    private static readonly HashSet<string> ForbiddenWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "command", "cmd", "shell", "script", "bash", "powershell", "pwsh",
        "exec", "execute", "eval", "args", "argv", "arguments", "cmdline", "commandline",
    };

    // Concrete commands in the closed vocabulary: every non-abstract AgentCommand in the Contracts assembly.
    private static IReadOnlyList<Type> ConcreteCommands() =>
        [.. ContractsAssembly.GetExportedTypes()
            .Where(t => typeof(AgentCommand).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .OrderBy(t => t.Name)];

    [Test]
    public async Task The_command_vocabulary_is_non_empty_and_closed()
    {
        IReadOnlyList<Type> commands = ConcreteCommands();

        // If this is empty the reflection is wrong, not the boundary — every later assertion would pass vacuously.
        await Assert.That(commands).IsNotEmpty();

        // Closed means sealed: an open command type could be subclassed elsewhere to smuggle in a member the
        // guard below never sees.
        foreach (Type command in commands)
        {
            await Assert.That(command.IsSealed)
                .IsTrue()
                .Because($"{command.Name} is an AgentCommand and must be sealed (the vocabulary is closed).");
        }
    }

    [Test]
    public async Task No_command_carries_a_free_form_command_script_or_shell_string()
    {
        List<string> offenders = [];

        foreach (Type command in ConcreteCommands())
        {
            foreach ((Type owner, PropertyInfo member) in ReachableTextMembers(command))
            {
                if (SplitWords(member.Name).Any(ForbiddenWords.Contains))
                {
                    offenders.Add($"{owner.Name}.{member.Name} ({member.PropertyType.Name})");
                }
            }
        }

        await Assert.That(offenders)
            .IsEmpty()
            .Because(
                "trust-boundaries.md §9 rule 3: a Web → Agent command must never carry a free-form command, "
                + "script or shell string. Reintroduced under: " + string.Join(", ", offenders));
    }

    [Test]
    public async Task The_only_operator_authored_line_is_the_policy_gated_console_input()
    {
        // The single command that carries an operator-authored free-text line is the F28 console. Its field is
        // named Input, not Command/Cmd/Script, on purpose (ADR 0032; the closed-vocabulary canary). If someone
        // renames it to a shell-shaped name, the guard above reddens — this asserts the canary is present at all.
        PropertyInfo? input = typeof(ExecuteConsoleCommand).GetProperty(nameof(ExecuteConsoleCommand.Input));

        await Assert.That(input).IsNotNull();
        await Assert.That(input!.PropertyType).IsEqualTo(typeof(string));
        await Assert.That(SplitWords(input.Name).Any(ForbiddenWords.Contains))
            .IsFalse()
            .Because("the console line is named 'Input' precisely so it is not a free-form command name.");
    }

    // Every text-bearing member reachable from a command: its own string/collection-of-string properties, and
    // those of any Contracts record it composes (e.g. ConfigApply → ConfigValueEdit), walked once per type.
    private static IEnumerable<(Type Owner, PropertyInfo Member)> ReachableTextMembers(Type root)
    {
        HashSet<Type> seen = [];
        Queue<Type> frontier = new();
        frontier.Enqueue(root);

        while (frontier.Count > 0)
        {
            Type type = frontier.Dequeue();
            if (!seen.Add(type))
            {
                continue;
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Type propertyType = property.PropertyType;

                if (CarriesText(propertyType))
                {
                    yield return (type, property);
                }

                // Follow composed Contracts records (and the element type of collections) so a free-form
                // command hidden one payload deep is still caught.
                foreach (Type candidate in ComposedContractTypes(propertyType))
                {
                    if (!seen.Contains(candidate))
                    {
                        frontier.Enqueue(candidate);
                    }
                }
            }
        }
    }

    private static bool CarriesText(Type type) =>
        type == typeof(string) || ElementType(type) == typeof(string);

    private static IEnumerable<Type> ComposedContractTypes(Type type)
    {
        Type target = ElementType(type) ?? type;
        if (target.Assembly == ContractsAssembly && !target.IsEnum && !target.IsPrimitive && target != typeof(string))
        {
            yield return target;
        }
    }

    // The element type of a generic enumerable (IReadOnlyList<T>, List<T>, T[]), or null if not enumerable.
    private static Type? ElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
        {
            return type.GetGenericArguments().FirstOrDefault();
        }

        return null;
    }

    // Split a PascalCase/camelCase identifier into its words: "ShellCommand" → ["Shell","Command"].
    private static IEnumerable<string> SplitWords(string name)
    {
        int start = 0;
        for (int i = 1; i <= name.Length; i++)
        {
            bool boundary = i == name.Length
                || (char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                || (char.IsUpper(name[i]) && i + 1 < name.Length && char.IsLower(name[i + 1]));

            if (boundary)
            {
                yield return name[start..i];
                start = i;
            }
        }
    }
}
