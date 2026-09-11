using System.Reflection;
using ZWarden.Domain.Security;

namespace ZWarden.Domain.Tests.Security;

/// <summary>
/// S4: the structural invariants that make a secret unable to leak by accident (PRD 10). Behavioural
/// coverage is in <see cref="SecretTests"/>; these assert the shape of the types by reflection, so a
/// future edit that (say) adds an implicit string conversion is a red build.
/// </summary>
public class SecretTypeInvariantTests
{
    [Test]
    [Arguments(typeof(SecretString))]
    [Arguments(typeof(Secret<>))]
    public async Task Secret_types_declare_no_conversion_to_string(Type secretType)
    {
        MethodInfo[] toStringConversions = secretType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name is "op_Implicit" or "op_Explicit" && m.ReturnType == typeof(string))
            .ToArray();

        await Assert.That(toStringConversions).IsEmpty();
    }

    [Test]
    [Arguments(typeof(SecretString))]
    [Arguments(typeof(Secret<>))]
    public async Task Secret_types_override_ToString(Type secretType)
    {
        MethodInfo toString = secretType.GetMethod(nameof(ToString), Type.EmptyTypes)!;

        await Assert.That(toString.DeclaringType).IsEqualTo(secretType);
    }
}
