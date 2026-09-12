using System.Net;
using System.Text;
using ZWarden.Agent.Trust;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Agent.Tests;

/// <summary>
/// F9 S9 (PR 2) test plan item 13: the HTTPS enrollment client maps a successful response to trust material
/// and treats the server's generic refusal (or a malformed body) as no material (ADR 0007).
/// </summary>
public class HttpEnrollmentClientTests
{
    private static HttpEnrollmentClient Client(HttpStatusCode status, string? json)
    {
        HttpClient http = new(new StubHandler(status, json)) { BaseAddress = new Uri("https://localhost/") };
        return new HttpEnrollmentClient(http, new RecordingLogger<HttpEnrollmentClient>());
    }

    [Test]
    public async Task A_successful_exchange_returns_trust_material()
    {
        AgentId agent = AgentId.New();
        string json = $"{{\"agentId\":\"{agent}\",\"agentCredential\":\"zwa_the-credential\",\"label\":\"host-alpha\"}}";

        AgentTrustMaterial? material = await Client(HttpStatusCode.OK, json).EnrollAsync(new SecretString("zwe_secret"));

        await Assert.That(material).IsNotNull();
        await Assert.That(material!.AgentId).IsEqualTo(agent);
        await Assert.That(material.Credential.Reveal()).IsEqualTo("zwa_the-credential");
        await Assert.That(material.Label).IsEqualTo("host-alpha");
    }

    [Test]
    public async Task A_refused_exchange_returns_null()
    {
        AgentTrustMaterial? material = await Client(HttpStatusCode.Unauthorized, null)
            .EnrollAsync(new SecretString("zwe_secret"));

        await Assert.That(material).IsNull();
    }

    [Test]
    public async Task A_malformed_success_body_returns_null()
    {
        AgentTrustMaterial? material = await Client(HttpStatusCode.OK, "{\"agentId\":\"\",\"agentCredential\":\"\"}")
            .EnrollAsync(new SecretString("zwe_secret"));

        await Assert.That(material).IsNull();
    }

    private sealed class StubHandler(HttpStatusCode status, string? json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(status);
            if (json is not null)
            {
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }
}
