using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Web.Components.Pages.Servers.Sections;

/// <summary>
/// The Config editor's unsaved drafts that aren't on screen (#322 live pass): one per Server and file, kept while the
/// operator opens another rail section or config file tab and handed back when they return. Scoped, so it lives as
/// long as the circuit, in server memory only (the values include secrets). A draft with nothing unsaved isn't kept,
/// so coming back to a clean file reads it fresh.
/// </summary>
public sealed class ConfigDraftStore
{
    private readonly Dictionary<(ServerId Server, PzConfigFile File), ConfigEditorDraft> _drafts = [];

    /// <summary>Keeps <paramref name="draft"/> for <paramref name="server"/> if it holds unsaved edits; otherwise
    /// forgets any draft kept for that file.</summary>
    public void Keep(ServerId server, ConfigEditorDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.HasUnsavedState)
        {
            _drafts[(server, draft.File)] = draft;
        }
        else
        {
            _drafts.Remove((server, draft.File));
        }
    }

    /// <summary>Hands back (and forgets) the draft kept for <paramref name="server"/>'s <paramref name="file"/>.</summary>
    public ConfigEditorDraft? Take(ServerId server, PzConfigFile file) =>
        _drafts.Remove((server, file), out ConfigEditorDraft? draft) ? draft : null;
}
