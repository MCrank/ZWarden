namespace ZWarden.Web.Hosting;

/// <summary>Registers the <see cref="FormLimitGuardMiddleware"/> (#224).</summary>
public static class FormLimitGuardExtensions
{
    /// <summary>Adds the oversized-form guard. Place it after authorization and before antiforgery, so only an
    /// authorized post is read and antiforgery sees the already-buffered form.</summary>
    public static IApplicationBuilder UseFormLimitGuard(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<FormLimitGuardMiddleware>();
    }
}
