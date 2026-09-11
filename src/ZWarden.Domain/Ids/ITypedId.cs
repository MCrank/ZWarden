namespace ZWarden.Domain.Ids;

/// <summary>
/// Non-generic view of a typed identifier, so code and converter factories can handle any of
/// them without knowing the concrete type.
/// </summary>
public interface ITypedId
{
    /// <summary>The underlying time-ordered UUIDv7.</summary>
    Guid Value { get; }
}

/// <summary>
/// A strongly-typed, prefix-scoped UUIDv7 identifier (PRD 8). <typeparamref name="TSelf"/> is the
/// concrete struct (e.g. <c>AgentId</c>). To add one: declare a <c>readonly record struct X(Guid
/// Value) : ITypedId&lt;X&gt;</c>, give it a unique lowercase <see cref="Prefix"/>, and delegate
/// the members to <see cref="TypedId"/>. See ADR 0014; the registry test enforces the prefix rules.
/// </summary>
/// <typeparam name="TSelf">The concrete identifier struct.</typeparam>
public interface ITypedId<TSelf> : ITypedId
    where TSelf : struct, ITypedId<TSelf>
{
    /// <summary>The lowercase-ASCII prefix including its trailing hyphen, e.g. <c>"agt-"</c>.</summary>
    static abstract string Prefix { get; }

    /// <summary>Wraps a raw <see cref="Guid"/> as this identifier type.</summary>
    static abstract TSelf FromGuid(Guid value);
}
