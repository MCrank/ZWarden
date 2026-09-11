using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Ids;

/// <summary>
/// EF Core value converters mapping any typed id to its native <see cref="Guid"/> column
/// (ADR 0004: store the native uuid, no provider branch). The F2 model convention applies these
/// automatically to every <see cref="ITypedId"/> property.
/// </summary>
public static class TypedIdValueConverters
{
    /// <summary>The <typeparamref name="T"/> ↔ <see cref="Guid"/> converter for a single id type.</summary>
    public static ValueConverter<T, Guid> For<T>()
        where T : struct, ITypedId<T>
        => new(id => id.Value, value => TypedId.FromGuid<T>(value));

    /// <summary>The converter for a typed-id type known only at runtime (used by the model convention).</summary>
    public static ValueConverter For(Type idType)
    {
        ArgumentNullException.ThrowIfNull(idType);
        MethodInfo generic = typeof(TypedIdValueConverters)
            .GetMethod(nameof(For), genericParameterCount: 1, Type.EmptyTypes)!
            .MakeGenericMethod(idType);
        return (ValueConverter)generic.Invoke(null, null)!;
    }
}
