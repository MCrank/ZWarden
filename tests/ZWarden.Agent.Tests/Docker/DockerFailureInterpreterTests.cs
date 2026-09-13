using System.Net;
using ZWarden.Agent.Docker;

namespace ZWarden.Agent.Tests.Docker;

/// <summary>
/// F13 S3 (T6): the create-failure interpreter. The allowlist denies <c>/images/*</c> (ADR 0008), so a
/// missing canonical image cannot be pre-checked — it surfaces as a clean <c>404 No such image</c> at create,
/// which the interpreter turns into an actionable, operator-fixable diagnostic.
/// </summary>
public class DockerFailureInterpreterTests
{
    [Test]
    public async Task A_404_no_such_image_is_the_pre_provision_failure()
    {
        ContainerCreateFailure failure = DockerFailureInterpreter.InterpretCreate(
            HttpStatusCode.NotFound, "{\"message\":\"No such image: zwarden-pzserver@sha256:abc\"}");

        await Assert.That(failure).IsEqualTo(ContainerCreateFailure.ImageNotProvisioned);
    }

    [Test]
    public async Task The_no_such_image_match_is_case_insensitive()
    {
        ContainerCreateFailure failure = DockerFailureInterpreter.InterpretCreate(
            HttpStatusCode.NotFound, "no such IMAGE: x");

        await Assert.That(failure).IsEqualTo(ContainerCreateFailure.ImageNotProvisioned);
    }

    [Test]
    public async Task A_404_for_another_reason_is_not_the_pre_provision_failure()
    {
        ContainerCreateFailure failure = DockerFailureInterpreter.InterpretCreate(
            HttpStatusCode.NotFound, "{\"message\":\"No such container\"}");

        await Assert.That(failure).IsEqualTo(ContainerCreateFailure.Unknown);
    }

    [Test]
    public async Task A_no_such_image_message_on_a_non_404_status_is_not_matched()
    {
        ContainerCreateFailure failure = DockerFailureInterpreter.InterpretCreate(
            HttpStatusCode.InternalServerError, "No such image");

        await Assert.That(failure).IsEqualTo(ContainerCreateFailure.Unknown);
    }

    [Test]
    public async Task A_null_body_is_unknown()
    {
        await Assert.That(DockerFailureInterpreter.InterpretCreate(HttpStatusCode.NotFound, null))
            .IsEqualTo(ContainerCreateFailure.Unknown);
    }
}
