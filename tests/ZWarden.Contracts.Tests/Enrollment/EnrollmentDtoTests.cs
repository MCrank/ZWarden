using System.Text.Json;
using ZWarden.Contracts.Enrollment;
using ZWarden.Contracts.Protocol;

namespace ZWarden.Contracts.Tests.Enrollment;

/// <summary>
/// F9 S6 (PR 2): the enrollment-exchange DTOs round-trip through <see cref="System.Text.Json"/> and are
/// deliberately <b>outside</b> the closed protocol vocabulary — plain contracts for the pre-trust bootstrap,
/// not <c>IProtocolMessage</c> leaves, so the closed-vocabulary guard is unaffected (ADR 0007).
/// </summary>
public class EnrollmentDtoTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task EnrollmentRequest_round_trips()
    {
        EnrollmentRequest request = new("zwe_secret-value");
        EnrollmentRequest? back = JsonSerializer.Deserialize<EnrollmentRequest>(
            JsonSerializer.Serialize(request, Web), Web);

        await Assert.That(back).IsEqualTo(request);
    }

    [Test]
    public async Task EnrollmentResponse_round_trips_with_and_without_a_label()
    {
        EnrollmentResponse labelled = new("agt-019c0000000070008000000000000001", "zwa_credential", "host-alpha");
        EnrollmentResponse unlabelled = new("agt-019c0000000070008000000000000002", "zwa_credential", null);

        await Assert.That(JsonSerializer.Deserialize<EnrollmentResponse>(JsonSerializer.Serialize(labelled, Web), Web))
            .IsEqualTo(labelled);
        await Assert.That(JsonSerializer.Deserialize<EnrollmentResponse>(JsonSerializer.Serialize(unlabelled, Web), Web))
            .IsEqualTo(unlabelled);
    }

    [Test]
    public async Task The_dtos_are_not_protocol_messages()
    {
        await Assert.That(typeof(IProtocolMessage).IsAssignableFrom(typeof(EnrollmentRequest))).IsFalse();
        await Assert.That(typeof(IProtocolMessage).IsAssignableFrom(typeof(EnrollmentResponse))).IsFalse();
    }
}
