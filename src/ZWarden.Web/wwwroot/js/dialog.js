// Confirmation dialogs for destructive actions (#271) — static-SSR progressive enhancement over a native <dialog>.
// The page renders the dialog (holding an ordinary static form) closed; this opens it, closes it, and keeps its submit
// disabled until the typed confirmation matches. The server re-checks the confirmation on post, so this is a
// convenience, never the guard. Without JS the dialog never opens, so nothing can be submitted from it (fail closed).
//   [data-zw-dialog-open="<name>"]      opens  <dialog data-zw-dialog="<name>">
//   [data-zw-dialog-close]              closes the dialog it is in
//   [data-zw-confirm-expected="<text>"] an input that must equal <text> exactly to enable the form's
//   [data-zw-confirm-submit]            submit button.
// Delegated on document so it survives Blazor enhanced-navigation DOM merges.
(function () {
  'use strict';
  var doc = document;

  function sync(input) {
    var form = input.form;
    if (!form) { return; }
    var matches = input.value === input.getAttribute('data-zw-confirm-expected');
    form.querySelectorAll('[data-zw-confirm-submit]').forEach(function (button) { button.disabled = !matches; });
  }

  function reset(dialog) {
    dialog.querySelectorAll('[data-zw-confirm-expected]').forEach(function (input) {
      input.value = '';
      sync(input);
    });
  }

  doc.addEventListener('click', function (e) {
    var opener = e.target.closest && e.target.closest('[data-zw-dialog-open]');
    if (opener) {
      var dialog = doc.querySelector('dialog[data-zw-dialog="' + opener.getAttribute('data-zw-dialog-open') + '"]');
      if (dialog && typeof dialog.showModal === 'function' && !dialog.open) {
        e.preventDefault();
        reset(dialog);
        dialog.showModal();
        var first = dialog.querySelector('[data-zw-confirm-expected]');
        if (first) { first.focus(); }
      }
      return;
    }

    var closer = e.target.closest && e.target.closest('[data-zw-dialog-close]');
    if (closer) {
      var open = closer.closest('dialog');
      if (open) {
        e.preventDefault();
        open.close();
      }
    }
  });

  doc.addEventListener('input', function (e) {
    if (e.target.matches && e.target.matches('[data-zw-confirm-expected]')) { sync(e.target); }
  });
})();
