using Microsoft.AspNetCore.Antiforgery;

namespace ZWarden.Web.Hosting;

/// <summary>
/// Turns a browser form post the form reader refuses (over its value-count/length limits, #224) into a logged
/// warning and a redirect back to the same page with <c>formRejected=too-large</c>, instead of a bare, unlogged
/// HTTP 400 before any page code runs. Scoped to endpoints that validate antiforgery — the static-SSR forms — so
/// API and hub traffic is untouched. It reads the form early with the endpoint's own limits (routing has already
/// applied any <c>[RequestFormLimits]</c>), so the later antiforgery and form-mapping reads reuse the buffered form.
/// </summary>
public sealed partial class FormLimitGuardMiddleware
{
    /// <summary>The query parameter a page reads to explain that its post was refused.</summary>
    public const string RejectedQueryKey = "formRejected";

    private readonly RequestDelegate _next;
    private readonly ILogger<FormLimitGuardMiddleware> _logger;

    public FormLimitGuardMiddleware(RequestDelegate next, ILogger<FormLimitGuardMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        HttpRequest request = context.Request;
        bool guarded = HttpMethods.IsPost(request.Method)
            && request.HasFormContentType
            && context.GetEndpoint()?.Metadata.GetMetadata<IAntiforgeryMetadata>() is { RequiresValidation: true };

        if (guarded)
        {
            try
            {
                await request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            }
            catch (InvalidDataException ex)
            {
                LogFormRejected(request.Path, ex.Message);
                string query = request.QueryString.Add(RejectedQueryKey, "too-large").ToUriComponent();
                // A local, same-page target (path + existing query only), so this is never an open redirect; 303 so
                // the browser follows with a GET.
                context.Response.StatusCode = StatusCodes.Status303SeeOther;
                context.Response.Headers.Location = $"{request.PathBase}{request.Path}{query}";
                return;
            }
        }

        await _next(context).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a form post to {Path}: {Reason}")]
    private partial void LogFormRejected(PathString path, string reason);
}
