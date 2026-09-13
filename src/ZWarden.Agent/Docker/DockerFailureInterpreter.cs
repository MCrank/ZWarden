using System.Net;

namespace ZWarden.Agent.Docker;

/// <summary>How a <c>POST /containers/create</c> failed, in terms the operator can act on.</summary>
public enum ContainerCreateFailure
{
    /// <summary>An unclassified daemon error; the raw detail is carried alongside.</summary>
    Unknown,

    /// <summary>
    /// The canonical PZ image is not present locally, and the allowlist denies pulling it (ADR 0008 §3.4). The
    /// fix is operator-facing: pre-provision the image (<c>docker pull</c> / <c>compose pull</c>). The daemon's
    /// signal is a clean <c>404 No such image</c> (measured, research §3.4).
    /// </summary>
    ImageNotProvisioned,
}

/// <summary>
/// Interprets a Docker API failure into an actionable <see cref="ContainerCreateFailure"/>. Pure — it works on
/// the status code and response body alone — so the mapping is unit-tested without a daemon. The one case that
/// matters for F13 is the pre-provision failure: denying <c>/images/*</c> means create cannot pull the image,
/// and the daemon returns a clean 404 the Agent turns into a specific diagnostic rather than an opaque error.
/// </summary>
public static class DockerFailureInterpreter
{
    /// <summary>Classifies a create failure from its HTTP status and response body.</summary>
    public static ContainerCreateFailure InterpretCreate(HttpStatusCode status, string? responseBody)
    {
        if (status == HttpStatusCode.NotFound
            && responseBody is not null
            && responseBody.Contains("No such image", StringComparison.OrdinalIgnoreCase))
        {
            return ContainerCreateFailure.ImageNotProvisioned;
        }

        return ContainerCreateFailure.Unknown;
    }
}
