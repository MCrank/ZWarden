using System.Reflection;
using ZWarden.Domain.Ids;

namespace ZWarden.Domain.Tests.Ids;

/// <summary>
/// Turns PRD 7's prefix rules into a red build (S2): every typed id must have a unique,
/// lowercase-ASCII, hyphen-terminated prefix, and the registry must be exactly the documented set.
/// </summary>
public class PrefixRegistryTests
{
    private static List<Type> TypedIdStructs() =>
        typeof(TenantId).Assembly.GetTypes()
            .Where(t => t is { IsValueType: true, IsGenericTypeDefinition: false }
                && Array.Exists(t.GetInterfaces(), i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ITypedId<>)))
            .ToList();

    private static string PrefixOf(Type t) =>
        (string)t.GetProperty(nameof(ITypedId<TenantId>.Prefix), BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

    [Test]
    public async Task Every_prefix_is_unique_lowercase_ascii_and_hyphen_terminated()
    {
        List<Type> types = TypedIdStructs();
        List<string> prefixes = types.ConvertAll(PrefixOf);

        foreach (string prefix in prefixes)
        {
            await Assert.That(prefix).IsNotEmpty();
            await Assert.That(prefix.EndsWith('-')).IsTrue();
            await Assert.That(prefix).IsEqualTo(prefix.ToLowerInvariant());
            await Assert.That(prefix.All(static c => c is (>= 'a' and <= 'z') or '-')).IsTrue();
        }

        await Assert.That(prefixes.Distinct().Count()).IsEqualTo(prefixes.Count);
    }

    [Test]
    public async Task The_registry_holds_exactly_the_documented_prefixes()
    {
        string[] expected =
        [
            "ten-", "usr-", "rol-", "agt-", "srv-", "op-", "aud-", "bkp-", "diag-", "enr-",
            "cfg-", "mod-", "wsi-", "mdp-", "ban-", "ply-", "prm-", "crt-", "ntf-",
        ];

        List<string> actual = TypedIdStructs().ConvertAll(PrefixOf);
        actual.Sort(StringComparer.Ordinal);
        Array.Sort(expected, StringComparer.Ordinal);

        await Assert.That(actual).IsEquivalentTo(expected);
    }
}
