using ZWarden.Application.Servers;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;

namespace ZWarden.Web.Components.Servers;

/// <summary>
/// What the operator has typed into the Deploy server sheet (#338), and the per-step validation that keeps Next from
/// advancing past a bad field. The rules are the #230 wizard's (<see cref="NewServerForm"/>, <see cref="HostPortInput"/>,
/// <see cref="ServerBranchView"/>, <see cref="InitialSettingsRules"/>); the service re-validates everything on Deploy.
/// </summary>
public sealed class DeployServerDraft
{
    /// <summary>Host, name and the initial settings.</summary>
    public const int Basics = 0;

    /// <summary>The Build 42 branch.</summary>
    public const int GameVersion = 1;

    /// <summary>Expected players, heap and the overcommit acknowledgement.</summary>
    public const int Memory = 2;

    /// <summary>The steps' names, in order.</summary>
    public static IReadOnlyList<string> StepNames { get; } = ["Basics", "Game version", "Memory"];

    public string? HostId { get; set; }

    public string? Name { get; set; }

    public string? PublicName { get; set; }

    public string? MaxPlayers { get; set; }

    public string? Password { get; set; }

    public string? GamePort { get; set; }

    public string? WelcomeMessage { get; set; }

    public bool Public { get; set; }

    /// <summary>The branch choice: <c>""</c> for the public default, a curated value, or <see cref="ServerBranchView.CustomChoice"/>.</summary>
    public string Branch { get; set; } = string.Empty;

    public string? CustomBranch { get; set; }

    public string? ExpectedPlayers { get; set; }

    public string? HeapGiB { get; set; }

    public bool AcknowledgeOvercommit { get; set; }

    /// <summary>Why <paramref name="step"/> can't be left yet, or <c>null</c> when it is valid. Basics also needs the
    /// chosen host to be one of <paramref name="deployable"/> (connected right now, with a usable PZ image — #364).</summary>
    public string? Validate(int step, IReadOnlyCollection<AgentId> deployable)
    {
        ArgumentNullException.ThrowIfNull(deployable);
        return step switch
        {
            Basics => ValidateBasics(deployable),
            GameVersion => ServerBranchView.TryResolve(Branch, CustomBranch, out _, out string? branchError) ? null : branchError,
            Memory => NewServerForm.TryResolveHeap(HeapGiB, ExpectedPlayers, out _, out string? heapError) ? null : heapError,
            _ => null,
        };
    }

    /// <summary>The first step that isn't valid, with its message; <c>null</c> when every step is.</summary>
    public (int Step, string Message)? FirstInvalid(IReadOnlyCollection<AgentId> deployable)
    {
        for (int step = Basics; step <= Memory; step++)
        {
            if (Validate(step, deployable) is { } error)
            {
                return (step, error);
            }
        }

        return null;
    }

    /// <summary>The registration request for a fully valid draft (call <see cref="FirstInvalid"/> first).</summary>
    public NewServerRequest ToRequest()
    {
        if (!AgentId.TryParse(HostId, out AgentId agent)
            || !HostPortInput.TryParse(GamePort, out int? gamePort, out _)
            || !ServerBranchView.TryResolve(Branch, CustomBranch, out string? branch, out _)
            || !NewServerForm.TryResolveHeap(HeapGiB, ExpectedPlayers, out long? heap, out _)
            || !NewServerForm.TryParseMaxPlayers(MaxPlayers, out int? maxPlayers, out _))
        {
            throw new InvalidOperationException("The draft is not valid.");
        }

        return new NewServerRequest(agent, Name!.Trim(), gamePort, heap, Settings(maxPlayers), AcknowledgeOvercommit, branch);
    }

    /// <summary>The heap the draft asks for, or <c>null</c> for the Agent's default.</summary>
    public long? HeapBytes => NewServerForm.TryResolveHeap(HeapGiB, ExpectedPlayers, out long? heap, out _) ? heap : null;

    /// <summary>The step whose fields a refused registration points at, so the sheet can go back to it.</summary>
    public static int StepFor(ServerRegisterFailure failure) => failure switch
    {
        ServerRegisterFailure.InvalidBranch => GameVersion,
        ServerRegisterFailure.InvalidHeap or ServerRegisterFailure.OverCapacity => Memory,
        _ => Basics,
    };

    /// <summary>Why a host whose Agent reported no usable PZ image can't take a server, and how to fix it (#364).</summary>
    public const string NoPzImageMessage =
        "That host has no Project Zomboid image configured. Set ZWARDEN_PZ_IMAGE in its Agent's .env and restart the Agent.";

    /// <summary>The operator-facing reason a registration was refused.</summary>
    public static string FailureMessage(ServerRegisterFailure? failure) => failure switch
    {
        ServerRegisterFailure.NotAuthorized => "You are not permitted to deploy servers.",
        ServerRegisterFailure.AgentNotFound => "That host is no longer known.",
        ServerRegisterFailure.InvalidPort => "That game port is not allowed.",
        ServerRegisterFailure.PortInUse => "That game port (or the one above it) is already used by another server on this host.",
        ServerRegisterFailure.InvalidHeap => "That heap is not allowed.",
        ServerRegisterFailure.InvalidSettings => "One of the settings is not allowed.",
        ServerRegisterFailure.InvalidBranch => "That branch is not allowed.",
        ServerRegisterFailure.NoPzImage => NoPzImageMessage,
        _ => "The server could not be deployed.",
    };

    private string? ValidateBasics(IReadOnlyCollection<AgentId> deployable)
    {
        if (string.IsNullOrWhiteSpace(HostId) || string.IsNullOrWhiteSpace(Name))
        {
            return "Choose a host and a name.";
        }

        if (!AgentId.TryParse(HostId, out AgentId agent) || !deployable.Contains(agent))
        {
            return "That host can't take a server right now. Choose a connected host that has a PZ image.";
        }

        if (!HostPortInput.TryParse(GamePort, out _, out string? error)
            || !NewServerForm.TryParseMaxPlayers(MaxPlayers, out _, out error))
        {
            return error;
        }

        return InitialSettingsRules.ValidatePublicName(Blank(PublicName))
            ?? InitialSettingsRules.ValidatePassword(string.IsNullOrEmpty(Password) ? null : Password)
            ?? InitialSettingsRules.ValidateWelcomeMessage(Blank(WelcomeMessage));
    }

    // Only the settings the operator filled in; an unticked "public" leaves PZ's default (not listed).
    private NewServerSettings? Settings(int? maxPlayers)
    {
        string? publicName = Blank(PublicName);
        string? password = string.IsNullOrEmpty(Password) ? null : Password;
        string? welcome = Blank(WelcomeMessage);
        bool? listed = Public ? true : null;
        return listed is null && publicName is null && maxPlayers is null && password is null && welcome is null
            ? null
            : new NewServerSettings(listed, publicName, maxPlayers, password, welcome);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
