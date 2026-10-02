// Per-browser view preferences for interactive pages (#299), e.g. the config editor's remembered collapse/expand-all
// (#243). A convenience only: storage may be unavailable (private mode, blocked site data), so every access is guarded
// and a missing preference reads as null.
export function get(key) {
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

export function set(key, value) {
  try {
    window.localStorage.setItem(key, value);
  } catch {
    // Storage unavailable: the preference lasts until the page reloads.
  }
}
