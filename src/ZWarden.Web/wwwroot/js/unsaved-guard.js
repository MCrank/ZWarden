// #331: warns before unsaved Config edits are lost. The edits live in the circuit (ConfigDraftStore), which survives
// moving between a Server's rail sections and config file tabs, but not a reload, a tab close or a move to another page.
// Blazor's NavigationLock doesn't see the shell's links on this page (enhanced navigation handles them before the
// circuit does), so link clicks are checked here, ahead of it, in the capture phase.
let guard = null; // { path, message } while there are unsaved edits

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

  if (window.confirm(guard.message)) {
    guard = null; // Leaving: don't ask again on the way out.
  } else {
    event.preventDefault();
    event.stopImmediatePropagation();
  }
}

function onBeforeUnload(event) {
  if (guard) {
    event.preventDefault();
    event.returnValue = ''; // The browser shows its own "Leave site?" text.
  }
}

window.addEventListener('click', onClick, true);
window.addEventListener('beforeunload', onBeforeUnload);

// Turns the warning on for the Server page at `path` (with the confirm text), or off with a null path.
export function set(path, message) {
  guard = path ? { path: path.toLowerCase(), message } : null;
}
