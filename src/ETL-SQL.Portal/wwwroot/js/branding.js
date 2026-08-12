import { installDialogAccessibility } from './dialog-a11y.js';

export async function applyPortalBranding() {
  try {
    const res = await fetch('/api/branding');
    if (!res.ok) return;
    const branding = await res.json();

    const name = firstValue(branding, 'DisplayName');
    const footer = firstValue(branding, 'FooterText');
    const logoUrl = firstValue(branding, 'LogoUrl');

    document.querySelectorAll('.portal-brand-name').forEach(el => {
      el.textContent = name || 'ETL-SQL Portal';
    });
    if (name) document.title = document.title.replace('ETL-SQL Portal', name).replace('ETL-SQL Portal', name);

    document.querySelectorAll('.portal-brand-footer').forEach(el => { el.textContent = footer || ''; });

    document.querySelectorAll('.topbar-brand').forEach(el => {
      const mark = el.querySelector('.brand-mark');
      const existing = el.querySelector('.portal-brand-logo');
      if (logoUrl) {
        if (existing) {
          existing.src = logoUrl;
        } else {
        const img = document.createElement('img');
        img.className = 'portal-brand-logo';
        img.src = logoUrl;
        img.alt = '';
        el.prepend(img);
        }
        mark?.classList.add('brand-mark-hidden');
      } else {
        existing?.remove();
        mark?.classList.remove('brand-mark-hidden');
      }
    });
  } catch { }
}

function firstValue(obj, key) {
  if (!obj) return '';
  const lower = key.charAt(0).toLowerCase() + key.slice(1);
  return obj[key] || obj[lower] || '';
}

export function initTheme() {
  installDialogAccessibility();
  const currentTheme = localStorage.getItem('portal-theme') || 'light';
  if (currentTheme === 'dark') {
    document.body.classList.add('theme-dark');
  } else {
    document.body.classList.remove('theme-dark');
  }

  // Bind the theme toggle button if it exists
  const toggleBtn = document.getElementById('themeToggleBtn');
  if (toggleBtn) {
    toggleBtn.addEventListener('click', () => {
      const isDark = document.body.classList.toggle('theme-dark');
      const nextTheme = isDark ? 'dark' : 'light';
      localStorage.setItem('portal-theme', nextTheme);
      
      // If we are on index.html with a report loaded in iframe, notify it!
      const iframe = document.querySelector('.report-viewer iframe');
      if (iframe && iframe.contentDocument) {
        if (isDark) {
          iframe.contentDocument.body.classList.add('theme-dark');
        } else {
          iframe.contentDocument.body.classList.remove('theme-dark');
        }
      }

      document.dispatchEvent(new CustomEvent('portal-theme-change', { detail: { theme: nextTheme, isDark } }));
    });
  }

  initResponsiveNavigation();
}

function initResponsiveNavigation() {
  const menuBtn = document.getElementById('mobileMenuBtn');
  if (!menuBtn || menuBtn.dataset.navigationReady === 'true') return;

  menuBtn.dataset.navigationReady = 'true';
  const sidebar = document.getElementById('sidebar');
  const drawer = sidebar || document.body.appendChild(document.createElement('aside'));
  if (!drawer.id) drawer.id = 'shellNavDrawer';
  drawer.classList.add('shell-nav-drawer');

  const mobilePanel = document.createElement('div');
  mobilePanel.className = 'shell-nav-mobile';
  mobilePanel.innerHTML = `
    <div class="shell-nav-header">
      <strong>Navigation</strong>
      <button type="button" class="shell-nav-close" data-dialog-close aria-label="Close navigation">×</button>
    </div>
    <div class="shell-nav-identity" aria-live="polite"></div>
    <nav class="shell-nav-links" aria-label="Portal destinations"></nav>
    <div class="shell-nav-actions">
      <button type="button" class="shell-nav-theme">Toggle theme</button>
      <button type="button" class="shell-nav-signout">Sign Out</button>
    </div>`;
  drawer.prepend(mobilePanel);

  const overlay = document.createElement('div');
  overlay.className = 'shell-nav-overlay';
  overlay.hidden = true;
  document.body.appendChild(overlay);

  menuBtn.setAttribute('aria-controls', drawer.id);
  menuBtn.setAttribute('aria-expanded', 'false');
  const closeBtn = mobilePanel.querySelector('.shell-nav-close');
  const links = mobilePanel.querySelector('.shell-nav-links');
  const identity = mobilePanel.querySelector('.shell-nav-identity');
  const media = window.matchMedia('(max-width: 768px)');
  const inerted = [];

  function refreshDrawer() {
    links.replaceChildren();
    document.querySelectorAll('.topbar-nav a').forEach(source => {
      if (source.hidden || getComputedStyle(source).display === 'none') return;
      const link = document.createElement('a');
      link.href = source.href;
      link.textContent = source.textContent;
      if (source.classList.contains('active')) link.classList.add('active');
      links.appendChild(link);
    });
    identity.textContent = document.getElementById('topbarUser')?.textContent?.trim() || '';
  }

  function setBackgroundInert(value) {
    const candidates = [...document.querySelectorAll('.topbar > *, .main-content, .portal-corner-links')]
      .filter(element => !drawer.contains(element));
    if (value) {
      candidates.forEach(element => {
        inerted.push([element, element.inert]);
        element.inert = true;
      });
      return;
    }
    inerted.splice(0).forEach(([element, previous]) => { element.inert = previous; });
  }

  function openDrawer() {
    if (!media.matches) return;
    refreshDrawer();
    drawer.classList.add('open');
    drawer.setAttribute('role', 'dialog');
    drawer.setAttribute('aria-modal', 'true');
    drawer.setAttribute('aria-label', 'Portal navigation');
    drawer.removeAttribute('aria-hidden');
    overlay.hidden = false;
    overlay.classList.add('open');
    document.body.classList.add('shell-drawer-open');
    menuBtn.setAttribute('aria-expanded', 'true');
    setBackgroundInert(true);
    closeBtn.focus();
  }

  function closeDrawer() {
    if (!drawer.classList.contains('open')) return;
    drawer.classList.remove('open');
    drawer.setAttribute('aria-hidden', 'true');
    overlay.classList.remove('open');
    overlay.hidden = true;
    document.body.classList.remove('shell-drawer-open');
    menuBtn.setAttribute('aria-expanded', 'false');
    setBackgroundInert(false);
  }

  function syncViewport() {
    if (media.matches) return;
    closeDrawer();
    drawer.removeAttribute('role');
    drawer.removeAttribute('aria-modal');
    drawer.removeAttribute('aria-label');
  }

  menuBtn.addEventListener('click', openDrawer);
  closeBtn.addEventListener('click', closeDrawer);
  overlay.addEventListener('click', closeDrawer);
  links.addEventListener('click', event => {
    if (event.target.closest('a')) closeDrawer();
  });
  mobilePanel.querySelector('.shell-nav-theme').addEventListener('click', () => {
    document.getElementById('themeToggleBtn')?.click();
  });
  mobilePanel.querySelector('.shell-nav-signout').addEventListener('click', () => {
    document.getElementById('logoutBtn')?.click();
  });
  media.addEventListener('change', syncViewport);
  syncViewport();
}
