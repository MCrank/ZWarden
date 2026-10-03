using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ZWarden.Infrastructure.Mods;

// Stores a DateTimeOffset as a UTC DateTime, the same mapping the other configurations inline: SQLite can't order or
// compare DateTimeOffset, so every provider gets the UTC instant.
internal static class UtcConverters
{
    public static readonly ValueConverter<DateTimeOffset?, DateTime?> Nullable = new(
        value => value == null ? null : value.Value.UtcDateTime,
        value => value == null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc), TimeSpan.Zero));
}
