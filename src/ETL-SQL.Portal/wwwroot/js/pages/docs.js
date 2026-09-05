/**
 * Page module for docs.html.
 *
 * Moved out of an inline <script type="module"> block so it is a file the type gate,
 * the linters and the parse check can all see. Behaviour is unchanged.
 */

import { auth } from '../api.js';
import { applyPortalBranding, initTheme } from '../branding.js';
import { getSessionIdentity, renderSessionIdentity } from '../session-identity.js';
import { applyNavigationSafely } from '../portal-nav.js';
import { renderPortalHeader } from '../portal-header.js';

renderPortalHeader();
initTheme();
applyPortalBranding();

if (auth.isLoggedIn()) {
  const identity = getSessionIdentity(auth.getToken());
  renderSessionIdentity(identity, document.getElementById('topbarUser'));

  const logoutBtn = document.getElementById('logoutBtn');
  if (logoutBtn) {
    logoutBtn.style.display = 'inline-block';
    logoutBtn.addEventListener('click', () => auth.logout());
  }

  applyNavigationSafely();
}
