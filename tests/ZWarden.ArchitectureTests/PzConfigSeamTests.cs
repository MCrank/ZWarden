using System.Reflection;
using ZWarden.PzConfig;

namespace ZWarden.ArchitectureTests;

/// <summary>
/// ADR 0010: <c>IPzConfigDocument</c> is a rule, not a convenience — "nothing library-shaped crosses
/// it". This reflects over every public member of the ZWarden.PzConfig assembly and asserts no Loretta
/// type appears anywhere in the surface, so the parser stays one implementation behind the seam and a
/// fork or the hand-rolled fallback can replace it without touching a single caller.
/// </summary>
public class PzConfigSeamTests
{
    private static readonly Assembly PzConfigAssembly = typeof(IPzConfigParser).Assembly;

    [Test]
    public async Task No_loretta_type_appears_in_the_public_surface()
    {
        List<string> leaks = [];

        foreach (Type type in PzConfigAssembly.GetExportedTypes())
        {
            foreach (Type referenced in ReferencedTypes(type))
            {
                if (IsLoretta(referenced))
                {
                    leaks.Add($"{type.FullName} exposes {referenced.FullName}");
                }
            }
        }

        await Assert.That(leaks).IsEmpty();
    }

    private static bool IsLoretta(Type type)
    {
        string? assemblyName = type.Assembly.GetName().Name;
        return assemblyName is not null && assemblyName.StartsWith("Loretta", StringComparison.Ordinal);
    }

    // Every type that shows up in a type's public surface: its base type and interfaces, and the
    // signatures of its public/protected members. Generic arguments are unwrapped so a
    // Something<LorettaType> would be caught too.
    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (type.BaseType is { } baseType)
        {
            foreach (Type t in Flatten(baseType))
            {
                yield return t;
            }
        }

        foreach (Type iface in type.GetInterfaces())
        {
            foreach (Type t in Flatten(iface))
            {
                yield return t;
            }
        }

        foreach (MemberInfo member in type.GetMembers(flags))
        {
            // Only public and protected members form the surface a consumer can see.
            foreach (Type signatureType in SignatureTypes(member))
            {
                foreach (Type t in Flatten(signatureType))
                {
                    yield return t;
                }
            }
        }
    }

    private static IEnumerable<Type> SignatureTypes(MemberInfo member) => member switch
    {
        MethodInfo method when IsVisible(method) =>
            [method.ReturnType, .. method.GetParameters().Select(p => p.ParameterType)],
        ConstructorInfo ctor when IsVisible(ctor) =>
            [.. ctor.GetParameters().Select(p => p.ParameterType)],
        PropertyInfo property => [property.PropertyType],
        FieldInfo field when field.IsPublic || field.IsFamily => [field.FieldType],
        EventInfo { EventHandlerType: { } handler } => [handler],
        _ => [],
    };

    private static bool IsVisible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    // Unwraps arrays, by-ref, pointers and generic arguments so a wrapper around a Loretta type is caught.
    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (Type t in Flatten(element))
            {
                yield return t;
            }
        }

        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments())
            {
                foreach (Type t in Flatten(argument))
                {
                    yield return t;
                }
            }
        }
    }
}
