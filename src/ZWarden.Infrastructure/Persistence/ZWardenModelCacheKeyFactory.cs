using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ZWarden.Infrastructure.Persistence;

/// <summary>
/// Keeps a secret-protecting <see cref="ZWardenDbContext"/> and a plain one from sharing a cached model.
/// The token-value encryption convention (F4) is present only when a context was given an
/// <c>ISecretProtector</c>, so the model differs; EF caches models per context type by default, which
/// would let whichever built first serve both. Folding <see cref="ZWardenDbContext.HasSecretProtection"/>
/// into the cache key makes the two variants distinct entries.
/// </summary>
internal sealed class ZWardenModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        ArgumentNullException.ThrowIfNull(context);
        return (context.GetType(), (context as ZWardenDbContext)?.HasSecretProtection ?? false, designTime);
    }
}
