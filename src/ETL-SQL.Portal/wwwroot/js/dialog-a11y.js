// Shared dialog accessibility behaviour for every Portal surface.
//
// A dialog needs four things beyond looking like one, and each of them is invisible to whoever
// tests with a mouse:
//
//   1. focus moves into it when it opens — otherwise the keyboard user is still somewhere behind it;
//   2. Tab stays inside it while it is open — otherwise they tab out into content the dialog is
//      supposedly blocking, with no way to tell they have left;
//   3. focus returns to whatever opened it when it closes — otherwise focus resets to the top of the
//      document and they tab back through the whole page to get where they were;
//   4. Escape closes it — the one dismissal gesture that does not require finding a target.
//
// This existed as three near-identical copies inside index.html, admin.html, and orchestrator.html,
// and not at all in studio.html or the JS-rendered modals. Three copies is not redundancy, it is
// three chances to fix a bug once and still ship it twice.
//
// The observer approach is deliberate: Portal modals are opened by setting `style.display` or
// toggling a class from a dozen different call sites. Watching for the change means a new dialog
// gets the behaviour without its author having to know this module exists — which is the only way
// a rule like this survives contact with a growing codebase.

const FOCUSABLE = 'button, [href], input:not([type=hidden]), select, textarea, '
  + '[tabindex]:not([tabindex="-1"])';

const focusableIn = dialog => [...dialog.querySelectorAll(FOCUSABLE)]
  .filter(el => !el.disabled && el.offsetParent !== null);

/** True when the element is currently presented to the user, by either convention Portal uses. */
function isOpen(dialog) {
  const style = getComputedStyle(dialog);
  return style.display !== 'none' && style.visibility !== 'hidden';
}

function openDialog(dialog, state) {
  if (state.open.has(dialog)) return;

  state.open.set(dialog, document.activeElement);
  dialog.removeAttribute('aria-hidden');

  focusableIn(dialog)[0]?.focus();

  const onKeyDown = event => {
    if (event.key === 'Escape') {
      // Let the page's own close handler run if it has one; otherwise close it here so Escape is
      // never a dead key.
      const closer = dialog.querySelector('[data-dialog-close]')
        || dialog.querySelector('[data-close], .modal-close');
      if (closer) closer.click();
      else hide(dialog);
      return;
    }
    if (event.key !== 'Tab') return;

    const items = focusableIn(dialog);
    if (items.length === 0) return;
    const first = items[0];
    const last = items[items.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  dialog.addEventListener('keydown', onKeyDown);
  state.handlers.set(dialog, onKeyDown);
}

function closeDialog(dialog, state) {
  if (!state.open.has(dialog)) return;

  const handler = state.handlers.get(dialog);
  if (handler) {
    dialog.removeEventListener('keydown', handler);
    state.handlers.delete(dialog);
  }

  dialog.setAttribute('aria-hidden', 'true');

  const returnTo = state.open.get(dialog);
  state.open.delete(dialog);
  // Only restore if the element is still on the page — a dialog that replaced its own opener
  // would otherwise throw or focus a detached node.
  if (returnTo?.isConnected && typeof returnTo.focus === 'function') returnTo.focus();
}

function hide(dialog) {
  if (dialog.classList.contains('open')) dialog.classList.remove('open');
  else dialog.style.display = 'none';
}

/**
 * Applies open/close behaviour to every dialog under `root`, now and as they appear.
 *
 * Idempotent: calling it twice on the same root does not double-bind, so a page that installs it
 * and a module that also installs it cannot fight.
 */
export function installDialogAccessibility(root = document) {
  if (root.__dialogA11yInstalled) return;
  root.__dialogA11yInstalled = true;

  const state = { open: new Map(), handlers: new Map() };

  const sync = dialog => (isOpen(dialog) ? openDialog : closeDialog)(dialog, state);

  const syncAll = () => root.querySelectorAll('[role="dialog"], [role="alertdialog"]').forEach(sync);

  // `style` and `class` are the two attributes Portal toggles to show a dialog; `childList` catches
  // dialogs rendered into the page after load, which is how every JS module builds its modals.
  const observer = new MutationObserver(mutations => {
    for (const mutation of mutations) {
      if (mutation.type === 'attributes') {
        const target = mutation.target;
        if (target.matches?.('[role="dialog"], [role="alertdialog"]')) sync(target);
      } else {
        syncAll();
      }
    }
  });

  observer.observe(root === document ? document.body : root, {
    subtree: true,
    childList: true,
    attributes: true,
    attributeFilter: ['style', 'class'],
  });

  syncAll();
  return () => observer.disconnect();
}
