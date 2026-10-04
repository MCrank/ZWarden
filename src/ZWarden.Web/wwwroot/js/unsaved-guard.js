// #331: warns before unsaved Config edits are lost. The edits live in the circuit (ConfigDraftStore), which survives
// moving between a Server's rail sections and config file tabs, but not a reload, a tab close or a move to another page.
// Blazor's NavigationLock doesn't see the shell's links on this page (enhanced navigation handles them before the
// circuit does), so link clicks are checked here, ahead of it, in the capture phase. A link to another page is held and
// handed to the page, which asks in a Blueprint dialog and navigates itself if the operator chooses to leave. A reload or
// a tab close can only get the browser's own prompt.
let guard = null; // { path, page } while there are unsaved edits; page is the .NET guard to ask

function onClick(event) {
  if (!guard || event.defaultPrevented || event.button !== 0
    || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
    return;
  }

  const link = event.target instanceof Element ? event.target.closest('a[href]') : null;
  if (!link || (link.target && link.target !== '_self') || link.hasAttribute('download')) {
    return;
  }

  const url = new URL(link.href, document.baseURI);
  if (url.origin === location.origin && url.pathname.replace(/\/$/, '').toLowerCase() === guard.path) {
    return; // Another section or file of this Server: the edits are kept.
  }

  event.preventDefault();
  event.stopImmediatePropagation();
  guard.page.invokeMethodAsync('AskToLeave', url.href);
}

function onBeforeUnload(event) {
  if (guard) {
    event.preventDefault();
    event.returnValue = ''; // The browser shows its own "Leave site?" text.
  }
}

window.addEventListener('click', onClick, true);
window.addEventListener('beforeunload', onBeforeUnload);

// Turns the warning on for the Server page at `path`, asking `page` (a DotNetObjectReference) before a link leaves it;
// a null path turns it off.
export function set(path, page) {
  guard = path ? { path: path.toLowerCase(), page } : null;
}
