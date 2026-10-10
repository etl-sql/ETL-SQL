/**
 * Page module for login.html.
 *
 * Moved out of an inline <script type="module"> block so it is a file the type gate,
 * the linters and the parse check can all see. Behaviour is unchanged.
 */

import { auth, authApi } from '../api.js';
import { applyPortalBranding } from '../branding.js';
import { getSessionIdentity } from '../session-identity.js';
import { asError } from '../http-error.js';
import { readLoginResponse, readAuthProvidersResponse, requiresPasswordChange } from '../auth-responses.js';

applyPortalBranding();

// Offer the SSO button only when the deployment has OIDC enabled. (The federated session hand-off
// happens on the callback page via sso-complete.js, so tokens never reach this page's URL.)
authApi.providers?.().then(value => {
  const p = readAuthProvidersResponse(value);
  if (p?.oidcEnabled) {
    const sso = document.getElementById('ssoSection');
    if (sso) sso.style.display = '';
    const btn = document.getElementById('ssoBtn');
    if (btn && p.oidcLoginUrl) btn.setAttribute('href', p.oidcLoginUrl);
  }
}).catch(() => { /* providers endpoint optional; default to local-only UI */ });

// If bounced here because MustChangePassword was enforced mid-session,
// show the change form immediately (user still holds a valid JWT).
const urlParams = new URLSearchParams(window.location.search);
if (urlParams.get('changePassword') === 'true' && auth.isLoggedIn()) {
  document.getElementById('mustChangeBanner')!.style.display = '';
  document.getElementById('loginForm')!.style.display  = 'none';
  document.getElementById('changeForm')!.style.display = '';
} else if (auth.isLoggedIn()) {
  window.location.href = '/index.html';
}

const $err    = document.getElementById('errorMsg')!;
const $banner = document.getElementById('mustChangeBanner')!;
const $login  = document.getElementById('loginForm')!;
const $change = document.getElementById('changeForm')!;

function showError(msg: string) {
  $err.textContent = msg;
  $err.classList.add('show');
}
function clearError() { $err.classList.remove('show'); }

// ── Login ──────────────────────────────────────────────────────────────────────
document.querySelector<HTMLButtonElement>('#loginBtn')!.addEventListener('click', doLogin);
document.getElementById('username')?.focus();
document.getElementById('username')?.addEventListener('keydown', e => {
  if (e.key === 'Enter') {
    if (document.querySelector<HTMLInputElement>('#password')!.value) doLogin();
    else document.getElementById('password')!.focus();
  }
});
document.getElementById('password')!.addEventListener('keydown', e => {
  if (e.key === 'Enter') doLogin();
});

async function doLogin() {
  clearError();
  const username = document.querySelector<HTMLInputElement>('#username')!.value.trim();
  const password = document.querySelector<HTMLInputElement>('#password')!.value;
  if (!username || !password) { showError('Username and password are required.'); return; }

  const btn = document.querySelector<HTMLButtonElement>('#loginBtn')!;
  btn.disabled = true; btn.textContent = 'Signing in…';

  try {
    const data = readLoginResponse(await authApi.login(username, password));
    const mustChangePassword = requiresPasswordChange(data.token);
    auth.setTokens(data.token, data.refreshToken);

    if (mustChangePassword) {
      $banner.style.display = '';
      $login.style.display  = 'none';
      $change.style.display = '';
    } else {
      window.location.href = '/index.html';
    }
  } catch (caught) {
    const err = asError(caught);
    const msg = err.status === 401 ? 'Invalid username or password.'
              : err.status === 423 || err.status === 429 ? 'Account locked. Try again in 15 minutes.'
              : err.message || 'Sign-in failed.';
    showError(msg);
  } finally {
    btn.disabled = false; btn.textContent = 'Sign In';
  }
}

// ── Change password ────────────────────────────────────────────────────────────
document.querySelector<HTMLButtonElement>('#changeBtn')!.addEventListener('click', async () => {
  clearError();
  const np = document.querySelector<HTMLInputElement>('#newPwd')!.value;
  const cp = document.querySelector<HTMLInputElement>('#confirmPwd')!.value;
  if (!np || np.length < 8) { showError('Password must be at least 8 characters.'); return; }
  if (np !== cp)             { showError('Passwords do not match.'); return; }

  const btn = document.querySelector<HTMLButtonElement>('#changeBtn')!;
  btn.disabled = true; btn.textContent = 'Saving…';

  // Read the account name before the invalidated token is dropped: on the mid-session
  // ?changePassword=true path the user never typed a username, so the token is the only source.
  const username = document.querySelector<HTMLInputElement>('#username')!.value.trim() || tokenUsername();

  try {
    const current = document.querySelector<HTMLInputElement>('#currentPwd')!.value
                 || document.querySelector<HTMLInputElement>('#password')!.value;
    await authApi.changePassword(current, np);
  } catch (caught) {
    const err = asError(caught);
    showError(err.message || 'Password change failed.');
    btn.disabled = false; btn.textContent = 'Set Password';
    return;
  }

  // The server invalidates every session for the account on a password change, so the token we
  // still hold is already dead. Navigating into the app with it bounced the user straight back
  // here with no explanation, which made the forced first-run change look like a failed sign-in.
  // Exchange the new password for a fresh session instead; clear first so the sign-in request
  // does not carry the invalidated bearer token.
  auth.clear();
  try {
    const data = readLoginResponse(await authApi.login(username, np));
    auth.setTokens(data.token, data.refreshToken);
    window.location.href = '/index.html';
  } catch {
    // The password *was* changed; only the automatic sign-in failed. Say so, rather than
    // reporting a password-change failure the user would try to repeat.
    $banner.style.display = 'none';
    $change.style.display = 'none';
    $login.style.display  = '';
    document.querySelector<HTMLInputElement>('#password')!.value = '';
    showError('Your password was changed. Sign in with your new password.');
    btn.disabled = false; btn.textContent = 'Set Password';
  }
});

/** Account name carried by the current session token, or '' when there is no readable token. */
function tokenUsername() {
  try {
    return getSessionIdentity(auth.getToken()).displayName || '';
  } catch {
    return '';
  }
}
