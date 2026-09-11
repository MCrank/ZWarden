namespace ZWarden.Domain;

/// <summary>
/// An entity carrying an optimistic-concurrency token (ADR 0005). The persistence layer stamps a
/// fresh <see cref="Version"/> on every insert and update; a write against a stale version raises a
/// concurrency conflict. A portable <see cref="Guid"/> so it behaves identically on SQLite (TEXT)
/// and PostgreSQL (native <c>uuid</c>) - unlike a provider-generated row version.
/// </summary>
public interface IVersioned
{
    /// <summary>The concurrency token. Owned by the persistence layer; do not set it by hand.</summary>
    Guid Version { get; set; }
}
