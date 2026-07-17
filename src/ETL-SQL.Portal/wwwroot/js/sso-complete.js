/* sso-complete.js — federated (OIDC) sign-in hand-off.
 * The /api/auth/oidc/callback page embeds the issued portal session in a JSON data-island; this
 * same-origin module stores it exactly like a password login (so the rest of the SPA is unchanged)
 * and forwards to the app. Tokens never appear in the URL/history. */
import { auth } from '/js/api.js';

function fail() { window.location.replace('/login.html?error=sso_failed'); }

try {
  const el = document.getElementById('sso-data');
  const data = el ? JSON.parse(el.textContent) : null;
  if (data && data.token) {
    auth.setTokens(data.token, data.refreshToken);
    const target = typeof data.redirect === 'string' && data.redirect.startsWith('/')
      ? data.redirect : '/index.html';
    window.location.replace(target);
  } else {
    fail();
  }
} catch {
  fail();
}
