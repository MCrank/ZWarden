// Tail helper for the server-detail live-logs island (#212). The scrollable element is the BbScrollArea
// viewport, marked with .zw-log-viewport; we read/set its native scroll so the panel can stick to the newest
// line without yanking an operator who has scrolled up to read history. No Blazor circuit state lives here —
// the component measures "was at the bottom" before appending and calls scrollToBottom after render if so.
(function () {
  'use strict';
  function viewport(root) {
    return root && root.querySelector ? root.querySelector('.zw-log-viewport') : null;
  }
  window.zwLiveLogs = {
    // Within ~2 line-heights of the bottom counts as "at the bottom" (tolerates sub-pixel rounding).
    isAtBottom: function (root) {
      var v = viewport(root);
      if (!v) { return true; }
      return (v.scrollHeight - v.scrollTop - v.clientHeight) <= 32;
    },
    scrollToBottom: function (root) {
      var v = viewport(root);
      if (v) { v.scrollTop = v.scrollHeight; }
    }
  };
})();
