/* sso-complete.js — federated (OIDC) sign-in hand-off.
 * The /api/auth/oidc/callback page embeds the issued portal session in a JSON data-island; this
 * same-origin module stores it exactly like a password login (so the rest of the SPA is unchanged)
 * and forwards to the app. Tokens never appear in the URL/history. */
import { auth } from './api.js';

function fail() { window.location.replace('/login.html?error=sso_failed'); }

function safeTarget(value: unknown): string {
  if (typeof value !== 'string' || !value.startsWith('/')) return '/index.html';
  try {
    return new URL(value, window.location.origin).origin === window.location.origin ? value : '/index.html';
  } catch {
    return '/index.html';
  }
}

try {
  const el = document.getElementById('sso-data');
  const parsed: unknown = el ? JSON.parse(el.textContent || '') : null;
  const data = parsed && typeof parsed === 'object' ? parsed as Record<string, unknown> : null;
  if (data && typeof data.token === 'string' && data.token) {
    const token: LoginResponse['token'] = data.token;
    const refreshToken: LoginResponse['refreshToken'] | undefined = typeof data.refreshToken === 'string' ? data.refreshToken : undefined;
    auth.setTokens(token, refreshToken);
    window.location.replace(safeTarget(data.redirect));
  } else {
    fail();
  }
} catch {
  fail();
}
