using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using ZWarden.Contracts.Enrollment;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Agent.Trust;

/// <summary>
/// The HTTPS <see cref="IEnrollmentClient"/> (F9). It POSTs an <see cref="EnrollmentRequest"/> to the control
/// plane's <c>/agent/enroll</c> endpoint over its configured <see cref="HttpClient"/> (base address derived
/// from <c>ControlPlaneUri</c>) and maps a successful <see cref="EnrollmentResponse"/> to trust material. A
/// non-success status — the server's single generic failure — returns <c>null</c>; the secret and credential
/// are never logged.
/// </summary>
public sealed partial class HttpEnrollmentClient : IEnrollmentClient
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpEnrollmentClient> _logger;

    public HttpEnrollmentClient(HttpClient http, ILogger<HttpEnrollmentClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AgentTrustMaterial?> EnrollAsync(
        SecretString enrollmentSecret,
        CancellationToken cancellationToken = default)
    {
        EnrollmentRequest request = new(enrollmentSecret.Reveal());
        using HttpResponseMessage response = await _http
            .PostAsJsonAsync("/agent/enroll", request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            LogRejected((int)response.StatusCode);
            return null;
        }

        EnrollmentResponse? body = await response.Content
            .ReadFromJsonAsync<EnrollmentResponse>(cancellationToken)
            .ConfigureAwait(false);

        if (body is null
            || string.IsNullOrWhiteSpace(body.AgentCredential)
            || !AgentId.TryParse(body.AgentId, out AgentId agentId))
        {
            LogMalformed();
            return null;
        }

        return new AgentTrustMaterial(agentId, new SecretString(body.AgentCredential), body.Label);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Enrollment was refused by the control plane (HTTP {StatusCode}).")]
    private partial void LogRejected(int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The enrollment response was missing or malformed.")]
    private partial void LogMalformed();
}
