// Which "last action failed" alerts this viewer dismissed (#266), remembered by operation id in this browser only.
// A convenience: storage may be unavailable (private mode, blocked site data), so every access is guarded and the
// alert simply stays visible. The interactive Server Detail page (#299) imports this module; the key is the one
// live-status.js used before, so dismissals made then still hold.
const KEY = 'zw-dismissed-failures';
const MAX = 50;

function read() {
  try {
    const raw = window.localStorage.getItem(KEY);
    const ids = raw ? JSON.parse(raw) : [];
    return Array.isArray(ids) ? ids : [];
  } catch {
    return [];
  }
}

export function isDismissed(id) {
  return typeof id === 'string' && read().indexOf(id) !== -1;
}

export function dismiss(id) {
  if (typeof id !== 'string') { return; }
  try {
    const ids = read().filter((x) => x !== id);
    ids.push(id);
    window.localStorage.setItem(KEY, JSON.stringify(ids.slice(-MAX)));
  } catch {
    // Storage unavailable: the dismissal lasts until the page reloads.
  }
}
