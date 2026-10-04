using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Web.Components.Pages.Servers.Sections;

/// <summary>
/// The Config editor's drafts for this circuit (#322 live pass, #331): one per Server and file, the one on screen and
/// those kept while the operator opens another rail section or config file tab. Scoped, so it lives as long as the
/// circuit, in server memory only (the values include secrets). It also answers whether anything is unsaved, so the
/// page can warn before a reload or a navigation away loses it.
/// </summary>
public sealed class ConfigDraftStore
{
    private readonly Dictionary<(ServerId Server, PzConfigFile File), ConfigEditorDraft> _drafts = [];

    /// <summary>Makes <paramref name="draft"/> the current draft of its file on <paramref name="server"/>. The store
    /// holds the live object, so later edits to it count.</summary>
    public void Track(ServerId server, ConfigEditorDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        _drafts[(server, draft.File)] = draft;
    }

    /// <summary>Drops the draft of <paramref name="file"/> on <paramref name="server"/> (its edits were applied).</summary>
    public void Forget(ServerId server, PzConfigFile file) => _drafts.Remove((server, file));

    /// <summary>The draft of <paramref name="file"/> on <paramref name="server"/> if it holds unsaved edits; a clean one
    /// is dropped, so coming back to that file reads it fresh.</summary>
    public ConfigEditorDraft? Find(ServerId server, PzConfigFile file)
    {
        if (!_drafts.TryGetValue((server, file), out ConfigEditorDraft? draft))
        {
            return null;
        }

        if (draft.HasUnsavedState)
        {
            return draft;
        }

        _drafts.Remove((server, file));
        return null;
    }

    /// <summary>Whether any of <paramref name="server"/>'s files has unsaved edits, apart from
    /// <paramref name="ignoring"/> (a file whose edits are already being applied).</summary>
    public bool HasUnsaved(ServerId server, PzConfigFile? ignoring = null) =>
        _drafts.Any(d => d.Key.Server == server && d.Key.File != ignoring && d.Value.HasUnsavedState);
}
