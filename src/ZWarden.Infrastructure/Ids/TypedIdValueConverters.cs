using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ZWarden.Domain.Ids;

namespace ZWarden.Infrastructure.Ids;

/// <summary>
/// EF Core value converters mapping any typed id to its native <see cref="Guid"/> column
/// (ADR 0004: store the native uuid, no provider branch). Feature 2 applies these in
/// <c>OnModelCreating</c> - one <c>HasConversion</c> per id property.
/// </summary>
public static class TypedIdValueConverters
{
    /// <summary>The <typeparamref name="T"/> ↔ <see cref="Guid"/> converter for a single id type.</summary>
    public static ValueConverter<T, Guid> For<T>()
        where T : struct, ITypedId<T>
        => new(id => id.Value, value => TypedId.FromGuid<T>(value));
}
