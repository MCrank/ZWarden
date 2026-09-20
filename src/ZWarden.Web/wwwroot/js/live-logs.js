// Tail helper for the server-detail live-logs island (#212). The scrollable element is the BbScrollArea viewport,
// marked with .zw-log-viewport. A scroll listener tracks whether the operator is pinned to the bottom; the panel
// calls tail() after each render, which re-pins to the newest line only while pinned — so scrolling up to read
// history is never interrupted, and new lines are followed live. Scrolling is done in requestAnimationFrame so it
// runs after the just-appended line has laid out and reaches the true bottom (the earlier measure-before-render
// approach could mis-time this and stop following). No Blazor circuit state lives here.
(function () {
  'use strict';
  function viewport(root) {
    return root && root.querySelector ? root.querySelector('.zw-log-viewport') : null;
  }
  // Within ~2 line-heights of the bottom counts as "at the bottom" (tolerates sub-pixel rounding).
  function atBottom(v) {
    return (v.scrollHeight - v.scrollTop - v.clientHeight) <= 32;
  }
  function scrollToEnd(v) {
    // After layout, so scrollHeight already includes the newest line.
    requestAnimationFrame(function () { v.scrollTop = v.scrollHeight; });
  }
  window.zwLiveLogs = {
    // Idempotently attach the scroll listener that maintains the pinned flag. Starts pinned.
    attach: function (root) {
      var v = viewport(root);
      if (!v || v.__zwTail) { return; }
      v.__zwTail = true;
      v.__zwPinned = true;
      v.addEventListener('scroll', function () { v.__zwPinned = atBottom(v); }, { passive: true });
    },
    // Re-pin to the newest line, but only while the operator hasn't scrolled up. Call after each render.
    tail: function (root) {
      var v = viewport(root);
      if (v && v.__zwPinned !== false) { scrollToEnd(v); }
    },
    // Force to the bottom and re-pin (used when the panel first opens).
    toBottom: function (root) {
      var v = viewport(root);
      if (v) { v.__zwPinned = true; scrollToEnd(v); }
    }
  };
})();
