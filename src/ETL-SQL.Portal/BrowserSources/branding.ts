import { installDialogAccessibility } from './dialog-a11y.js';

let brandingGeneration = 0;
let previousBrandName = 'ETL-SQL Portal';

export async function applyPortalBranding() {
  const generation = ++brandingGeneration;
  try {
    const res = await fetch('/api/branding');
    if (!res.ok) return;
    const branding: unknown = await res.json();
    if (generation !== brandingGeneration) return;
    renderPortalBranding({ displayName: firstValue(branding, 'DisplayName'),
      footerText: firstValue(branding, 'FooterText'), logoUrl: firstValue(branding, 'LogoUrl') });
  } catch {
    // Branding is cosmetic; a failed initial read leaves the current name and mark in place.
  }
}

export function renderPortalBranding(branding: PortalBrandingDto) {
    brandingGeneration++;
    const name = branding.displayName || 'ETL-SQL Portal';
    const footer = branding.footerText || '';
    const logoUrl = branding.logoUrl || '';

    document.querySelectorAll('.portal-brand-name').forEach(el => {
      el.textContent = name || 'ETL-SQL Portal';
    });
    document.title = document.title.replace(previousBrandName, name);
    previousBrandName = name;

    document.querySelectorAll('.portal-brand-footer').forEach(el => { el.textContent = footer || ''; });

    document.querySelectorAll('.topbar-brand').forEach(el => {
      const mark = el.querySelector('.brand-mark');
      const existing = el.querySelector<HTMLImageElement>('.portal-brand-logo');
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
}

function firstValue(obj: unknown, key: Capitalize<keyof PortalBrandingDto>): string {
  if (!obj || typeof obj !== 'object') return '';
  const lower = key.charAt(0).toLowerCase() + key.slice(1);
  const values = obj as Record<string, unknown>;
  const value = values[key] || values[lower] || '';
  return typeof value === 'string' ? value : '';
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
      const iframeDocument = document.querySelector<HTMLIFrameElement>('.report-viewer iframe')?.contentDocument;
      if (iframeDocument) {
        if (isDark) {
          iframeDocument.body.classList.add('theme-dark');
        } else {
          iframeDocument.body.classList.remove('theme-dark');
        }
      }

      document.dispatchEvent(new CustomEvent('portal-theme-change', { detail: { theme: nextTheme, isDark } }));
    });
  }

  initResponsiveNavigation();
}

function initResponsiveNavigation() {
  const menuElement = document.getElementById('mobileMenuBtn');
  if (!menuElement || menuElement.dataset.navigationReady === 'true') return;
  const menuBtn = menuElement;

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
  const closeBtn = mobilePanel.querySelector<HTMLButtonElement>('.shell-nav-close')!;
  const links = mobilePanel.querySelector<HTMLElement>('.shell-nav-links')!;
  const identity = mobilePanel.querySelector<HTMLElement>('.shell-nav-identity')!;
  const media = window.matchMedia('(max-width: 768px)');
  const inerted: Array<[HTMLElement, boolean]> = [];

  function refreshDrawer() {
    links.replaceChildren();
    document.querySelectorAll<HTMLAnchorElement>('.topbar-nav a').forEach(source => {
      if (source.hidden || getComputedStyle(source).display === 'none') return;
      const link = document.createElement('a');
      link.href = source.href;
      link.textContent = source.textContent;
      if (source.classList.contains('active')) link.classList.add('active');
      links.appendChild(link);
    });
    identity.textContent = document.getElementById('topbarUser')?.textContent?.trim() || '';
  }

  function setBackgroundInert(value: boolean) {
    const candidates = [...document.querySelectorAll<HTMLElement>('.topbar > *, .main-content, .portal-corner-links')]
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
    if (event.target instanceof Element && event.target.closest('a')) closeDrawer();
  });
  mobilePanel.querySelector<HTMLButtonElement>('.shell-nav-theme')!.addEventListener('click', () => {
    document.getElementById('themeToggleBtn')?.click();
  });
  mobilePanel.querySelector<HTMLButtonElement>('.shell-nav-signout')!.addEventListener('click', () => {
    document.getElementById('logoutBtn')?.click();
  });
  media.addEventListener('change', syncViewport);
  syncViewport();
}
