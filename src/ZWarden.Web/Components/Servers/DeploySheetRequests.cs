namespace ZWarden.Web.Components.Servers;

/// <summary>The Deploy server sheet's tabs (#339).</summary>
public enum DeploySheetTab
{
    /// <summary>Deploy a new server (#338).</summary>
    New,

    /// <summary>Adopt a discovered, unmanaged container (#339).</summary>
    Adopt,
}

/// <summary>
/// Lets another island on the Fleet page open the Deploy server sheet (#339): the adopt banner and the sheet are
/// separate interactive roots, but they share the page's circuit and so this scoped instance. The sheet subscribes
/// while it's rendered; with no sheet listening a request does nothing.
/// </summary>
public sealed class DeploySheetRequests
{
    /// <summary>Raised when something asks the sheet to open on a tab.</summary>
    public event Func<DeploySheetTab, Task>? OpenRequested;

    /// <summary>Asks the sheet to open on <paramref name="tab"/>.</summary>
    public Task RequestOpenAsync(DeploySheetTab tab) => OpenRequested?.Invoke(tab) ?? Task.CompletedTask;
}
